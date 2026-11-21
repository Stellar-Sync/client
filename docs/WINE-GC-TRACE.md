# Capture GC timing under Wine

The current investigation concerns uploads on Arch Linux, a Ryzen 7 5800X, and
32 GiB RAM. Batch counters show long cumulative GC pauses, but cannot identify
the time spent suspending managed threads versus doing collection work.

Dalamud runs a Windows .NET runtime inside Wine. Native Linux diagnostic tools
use a different IPC transport, so a native `dotnet-trace` attachment should not
be assumed to reach that runtime. The runtime can instead write its own EventPipe
trace at startup, avoiding the need to attach through IPC.

## Capture one short reproduction

1. Exit the game and XIVLauncher completely. Startup tracing must be configured
   before Dalamud's runtime starts; reloading the plugin alone is insufficient.
2. From the `mare-client` repository, run:

   ```bash
   bash tools/capture-wine-gc.sh
   ```

   The default command is `/usr/bin/xivlauncher-rb`, matching the locally installed
   desktop launcher. For a different native launcher, supply its real command:

   ```bash
   bash tools/capture-wine-gc.sh -- /path/to/your-launcher
   ```

3. Log in, perform one short upload that reproduces the stutters, and note its
   start/end log times. Keep the normal concurrency setting and other plugins
   for this capture so the comparison concerns the observed problem.
4. Exit the game normally to finalize the trace. Exit the launcher too if it
   remains open. Avoid a long gameplay session with startup tracing enabled.
5. Locate the generated `stellar-sync-gc-*.nettrace` files. The wrapper uses a
   relative filename so native and Wine-hosted runtimes can both open it. Files
   appear in each runtime's working directory; additional launcher/helper traces
   may be present. Different PIDs in filenames do not necessarily correspond to
   the Linux PID shown by `ps` for a Wine process.

For this installation, search the game and launcher locations, plus the directory
from which the wrapper was run:

```bash
find "/mnt/games/FFXIV/SquareEnix/FINAL FANTASY XIV - A Realm Reborn" \
     "$HOME/.local/share/dev.goats.xivlauncher" "$HOME/.xlcore" "$PWD" \
     -type f -name 'stellar-sync-gc-*.nettrace' -print 2>/dev/null
```

Keep the original `.nettrace` format and the corresponding upload/hitch log
excerpt. CPU-viewer conversions can discard GC timing information needed for
analysis. If several small traces were generated, keep them until the game
runtime's trace has been identified. If no game trace is produced, report that
instead of changing Wine prefixes or GC settings.

The variables apply only to this launch and its descendants. Launch normally
next time to stop capturing; no persistent settings need to be reverted.

## What is captured

The wrapper sets only:

```text
DOTNET_EnableEventPipe=1
DOTNET_EventPipeConfig=Microsoft-Windows-DotNETRuntime:1:4
DOTNET_EventPipeOutputPath=stellar-sync-gc-<run timestamp>-{pid}.nettrace
DOTNET_EventPipeCircularMB=40
DOTNET_EventPipeOutputStreaming=1
```

Keyword `1` selects GC events and level `4` is Informational. This first pass
targets collection and runtime suspension/restart timing. It does not enable
the CPU sample profiler, force collections, or change GC heap/latency settings.
The buffer size is hexadecimal: `40` means 64 MiB of internal buffering, not a
64 MiB limit on the streamed file. Output can continue growing until the process
exits, which is why a short reproduction is preferable.

The settings and direct-file capture mechanism are supported by .NET. The wrapper
has a native .NET smoke test, but game/Wine capture is not yet verified on this
installation. A successful native test does not establish Wine compatibility.

The capture should let us separate collection work from suspension delays and
correlate pauses with the upload window. Allocating plugins or responsible call
stacks may require a second, more detailed capture after the timing is examined.

Sources: [EventPipe startup tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe),
[diagnostic transports](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostic-port),
[GC performance investigation](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance).
