# Upload compression allocation benchmark

Run from the client repository root:

```bash
dotnet restore --source /tmp tests/StellarSync.Compression.Benchmarks/StellarSync.Compression.Benchmarks.csproj
dotnet run -c Release --no-restore --project tests/StellarSync.Compression.Benchmarks -- --repeats 3 --output compression-results.csv
```

The offline restore command assumes `K4os.Compression.LZ4.Legacy` 1.3.8 and its
dependency are already present in the local NuGet cache. Otherwise restore using
the repository's normal package source first.

The benchmark compares `File.ReadAllBytesAsync` plus the exact legacy `WrapHC`
against the linked production `UploadFileCompressor`. Both return an exactly sized
compressed `byte[]`. The corpus contains repeated, random, and mixed repeated/random
data at 16 KiB, 256 KiB, 2 MiB, and 8 MiB: approximately 30.8 MiB per pass.

Compatibility checks include empty/small/boundary-length files, byte-for-byte
equality with the installed legacy wrapper, decompression with `Unwrap`, SHA1, and
pre-cancellation. Temporary synthetic files are removed at the end.

Output reports elapsed time, total process-wide GC allocated bytes, collection
counts by generation, compressed output size, managed bytes after each pass, and
retained workspace bytes (private static byte-array fields inspected only by the
standalone benchmark).
The first pass includes startup effects; warmed passes alternate implementation
order. Explicit collections are outside timed passes and exist only in this
standalone benchmark. Generated files may be in the OS cache; this is not a cold
disk benchmark. The production helper retains at most one raw-input buffer and
one scratch buffer, each up to 64 MiB. Reuse reduces allocation churn while
retaining workspace; it does not eliminate the output buffers held by active uploads.

These results isolate synthetic serial file compression. They do not establish
in-game network throughput or prove the cause of a particular UI hitch.
