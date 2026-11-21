# Comparing upload concurrency

Open **Settings → Transfers** in the plugin. **Maximum Parallel Uploads** appears
beside the existing download setting and accepts **1–10**, with a default of **2**.
The selected limit is saved normally. Uploads always use a bounded limit;
unlimited uploads are not supported.

The limits control asynchronous file transfers, not dedicated operating-system
threads. Compression remains serial across character sync and CharaData uploads.
Bounded batches can prepare one next file while waiting for a transfer slot.
The slider limits simultaneous upload requests. A higher value allows more files
to transfer at once; it does not create more compression workers or divide a
single file into parallel transfers. Transport, network, and server limits still apply.

Settings are read when a batch starts. Starting a batch also updates the shared
HTTP request limit used by overlapping character and CharaData batches. Lowering
that limit lets active requests finish before more requests acquire slots.
Changing outfits replaces the previous character upload; it does not cancel a
separate CharaData upload. Disconnecting cancels both types.

## Record a baseline and compare

1. Start with one upload, then compare two and four. If throughput improves and
   resource use stays acceptable, test higher limits up to ten. Use the same
   client/server builds, network path, logging level, and background load.
2. Use previously unseen, valid mod files with similar compressed sizes and file
   counts for each production run. A repeat of the same outfit can be skipped by
   client verification or server deduplication. Use staging to repeat the exact
   same cold workload; retain the production cache.
3. Record the new Information-level `Upload batch starting` and `Upload batch
   complete` lines. Completion includes successful file count, compressed bytes,
   elapsed time, aggregate MiB/s, configured limit, peak active uploads, and time
   spent reading/compressing files. Compression time overlaps transfer time and
   should not be added to the batch duration.
4. Compare client CPU, memory, game frame responsiveness, and file/DB VM CPU,
   memory, and disk latency during the same window. For a shared physical disk,
   include hypervisor disk measurements. Count HTTP errors and cancellations;
   failed batches do not emit a success summary or publish character data.

If aggregate throughput rises as concurrency rises, overlapping requests helps
this workload. If aggregate throughput stays flat while requests take longer,
investigate the shared network, CPU, storage, or database limit. A peak below the
configured limit can mean there are too few files or compression cannot prepare
them quickly enough. The slider sets a ceiling rather than guaranteeing that
every slot will be used.

Each outstanding upload retains its full compressed buffer in client memory. The
server also buffers and decompresses simultaneous uploads, so higher limits can
increase resource use on both ends. The slider does not raise request body limits
or change Cloudflare's limits. The transfer UI still provides cancellation.

## Investigate stutters during uploads

Nonempty batches also emit `Upload batch process diagnostics` when they finish,
fail, or are canceled. This samples counters at the beginning and end of the
batch; it does not force a collection or change runtime GC settings.

- **GC collections gen0/gen1/gen2:** changes in collection counts.
- **GC pause:** change in cumulative time paused for managed garbage collection.
- **Managed allocations approximately:** managed bytes allocated during the
  window, including objects already collected. This is not retained memory or
  a peak-memory measurement.
- **Process CPU seconds:** CPU time consumed by the game process across its
  threads, including native game code. It is not compression CPU time or a
  percentage; concurrent CPU work can exceed the elapsed wall-clock duration.

All these measurements cover the whole process and its plugins. They do not
attribute a collection or allocation to Stellar Sync, and overlapping batches
can report the same process activity. A hitch warning attached to another
plugin's callback likewise does not identify what caused the pause. Capture
these diagnostics beside the batch summary and hitch warnings for comparison.
Large GC pause time makes GC profiling useful; allocation counts alone cannot
prove that GC caused a hitch. Small measured GC pause time directs attention
to CPU contention, scheduling, or blocking work during the upload.
For the Arch Linux/Wine client, use the [GC trace capture guide](WINE-GC-TRACE.md)
to collect collection/suspension timing directly from the hosted runtime.

The compression path still reads each whole source file and uses LZ4 high
compression at every upload limit. CPU cost and the compressed output arrays
held by outstanding uploads remain. The temporary-buffer change below reduces
allocation churn; the cause of the game stalls is not established by these logs.

## Temporary compression buffer reuse

The original path allocated a whole raw input array, an input-sized LZ4 scratch
array, and the exact output array for every file. The current implementation
reuses separate input and scratch arrays, and returns an independent output array
for each upload. It preserves `L09_HC` and the legacy eight-byte framing, including
the original fallback for incompressible data. Compatibility tests compare the
output byte-for-byte with the installed `LZ4Wrapper.WrapHC` implementation and
verify legacy decompression and raw-file SHA1.

Each reusable workspace retains at most 64 MiB, so the two caches retain at most
128 MiB total. Files exceeding that size can still be compressed, but their
temporary arrays are not retained. The first use of a size requiring larger
workspace still allocates. This exchanges bounded retained memory for less
repeated allocation; compressed output buffers remain additional memory.

A standalone synthetic benchmark used 12 files totaling 30.80 MiB raw and 12.93 MiB
compressed, with three warmed passes and alternating implementation order:

| Implementation | Allocated per pass | Elapsed per pass |
| --- | ---: | ---: |
| ReadAllBytes + WrapHC | 75.70 MiB | 322.78 ms |
| Reused input/scratch, same HC format | 13.93 MiB | 318.20 ms |

That is about 81.6% less managed allocation in this workload. Timing was similar;
these measurements do not establish faster game uploads or resolved stutters.
The test retained 16 MiB of workspace for its largest 8 MiB file. See the
[benchmark instructions and raw results](../tests/StellarSync.Compression.Benchmarks/README.md).

A separate [GC-pressure experiment](../tests/StellarSync.Compression.GcProbe/README.md)
compared legacy FAST and HC encoders under short-lived allocation pressure. It
did not reproduce the game's long pauses on the Linux/.NET 10 test host. The
game log's zero Gen2 collections means its pauses should not be described as a
confirmed large-object-heap/full-GC problem. Continue comparing the process-wide
diagnostic line with actual in-game stutters after the buffer-reuse change.

## Reported development runs

These runs came from a **previous experimental build**, including an unconstrained
mode that has since been removed. Current uploads are limited to 1–10 requests.
The runs used different uncached outfits, so they show historical observations
rather than a controlled ranking of the current concurrency settings:

| Mode | Files | Compressed MiB | Seconds | MiB/s | Peak active | Read/compression seconds |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 2 | 26 | 52.38 | 25.88 | 2.02 | 2 | 7.74 |
| 4 | 8 | 9.15 | 8.68 | 1.05 | 4 | 2.15 |
| 4 | 14 | 36.36 | 14.60 | 2.49 | 4 | 7.25 |
| Unconstrained | 23 | 23.79 | 10.64 | 2.24 | 11 | 3.27 |

The small four-upload run put about 91% of its bytes in one 8.32 MiB file.
Concurrent requests do not divide a single file into simultaneous transfers,
so a batch dominated by one file has little opportunity to use multiple slots.
The unconstrained run finished sooner than the larger four-upload run while
transferring less data and spending less time reading/compressing it. Its higher
request count alone does not establish a speed or responsiveness advantage.

## Correctness and validation

Every upload must receive a successful HTTP status before its hash is verified.
The alternative upload retries with munged bytes and restores the original buffer
afterward. Batch failure cancels and drains sibling uploads. Canceled or replaced
sessions cannot clear newer progress or restore verification after disconnect.

Run the dependency-free checks from `mare-client`:

```bash
dotnet run --project tests/StellarSync.Uploads.Tests
dotnet run --project tests/StellarSync.UploadManager.Tests
dotnet run --project tests/StellarSync.Compression.Tests
```

The scheduler/stream suite tests concurrency, serial preparation, bounded
lookahead, draining, shared slots, exact stream bytes, and cancellation. The manager
suite links production upload code with lightweight environment substitutes to
test configured limits, HTTP failures, fallback, session replacement, disconnects,
and independent CharaData cancellation. Actual game responsiveness and server
throughput require an in-game run.
The compression suite additionally checks legacy byte compatibility, independent
output ownership after workspace reuse, short reads, truncation, and cancellation
or failures during reads.
