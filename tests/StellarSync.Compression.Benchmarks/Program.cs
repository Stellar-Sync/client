using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using K4os.Compression.LZ4.Legacy;
using StellarSync.FileCache;

var repeats = 3;
string? output = null;
for (var index = 0; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--repeats":
            repeats = int.Parse(args[++index], CultureInfo.InvariantCulture);
            if (repeats is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(repeats));
            break;
        case "--output":
            output = args[++index];
            break;
        default:
            throw new ArgumentException("Usage: --repeats 3 [--output results.csv]");
    }
}

var temporary = Path.Combine(Path.GetTempPath(), "stellar-compression-benchmark-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var cohort = new List<Sample>();
    foreach (var pattern in new[] { "repeated", "random", "mixed" })
    foreach (var length in new[] { 16 * 1024, 256 * 1024, 2 * 1024 * 1024, 8 * 1024 * 1024 })
        cohort.Add(await CreateSampleAsync(temporary, pattern, length));

    var implementations = new (string Name, Func<string, CancellationToken, Task<byte[]>> Compress)[]
    {
        ("ReadAllBytes+WrapHC", BaselineAsync),
        ("Cached-workspace+legacy-L09_HC", UploadFileCompressor.CompressAsync),
    };
    var measurements = new List<Measurement>();
    Console.WriteLine($"Synthetic standalone benchmark: .NET {Environment.Version}, {Environment.OSVersion}, CPU count {Environment.ProcessorCount}.");
    Console.WriteLine("K4os.Compression.LZ4.Legacy 1.3.8; one serial compression at a time; returned results are exact byte arrays.");
    Console.WriteLine("First pass is not warmed by this benchmark; generated files may already be in the OS cache. No disk cache is cleared.");
    Console.WriteLine("Explicit full collections occur only between benchmark passes, outside the timed operation. They are not plugin behavior.");
    Console.WriteLine("Allocated bytes are process-wide GC allocation deltas; pooling can retain workspace while reducing allocation churn.");

    foreach (var implementation in implementations)
        measurements.Add(await MeasureAsync(implementation.Name, "first", cohort, implementation.Compress));

    var validation = new List<Sample>(cohort);
    foreach (var length in new[] { 0, 1, 7, 8, 15, 16, 255, 256, 257, 4095, 4096, 4097 })
    foreach (var pattern in new[] { "repeated", "random", "mixed" })
        validation.Add(await CreateSampleAsync(temporary, pattern, length));
    foreach (var sample in validation)
    {
        var expected = await BaselineAsync(sample.Path, CancellationToken.None);
        var actual = await UploadFileCompressor.CompressAsync(sample.Path, CancellationToken.None);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new InvalidDataException($"Legacy output differs byte-for-byte: {sample.Name}.");
        var restored = LZ4Wrapper.Unwrap(actual);
        if (restored.Length != sample.Length || !SHA1.HashData(restored).AsSpan().SequenceEqual(sample.Sha1))
            throw new InvalidDataException($"Legacy decompression/SHA1 validation failed: {sample.Name}.");
    }
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        try
        {
            await UploadFileCompressor.CompressAsync(cohort[0].Path, cancelled.Token);
            throw new InvalidOperationException("Pre-cancelled compression unexpectedly succeeded.");
        }
        catch (OperationCanceledException)
        {
        }
    }
    Console.WriteLine($"Compatibility passed for {validation.Count} samples: byte-for-byte legacy equality, Unwrap, raw length, and SHA1; pre-cancelled operation propagated cancellation.");

    for (var repeat = 1; repeat <= repeats; repeat++)
    {
        // Alternate ordering to reduce a persistent first/second implementation bias.
        var order = repeat % 2 == 0 ? implementations.Reverse() : implementations;
        foreach (var implementation in order)
            measurements.Add(await MeasureAsync(implementation.Name, $"warm-{repeat}", cohort, implementation.Compress));
    }

    var csv = new StringBuilder("implementation,phase,files,raw_bytes,compressed_bytes,elapsed_ms,allocated_bytes,gen0,gen1,gen2,managed_bytes_after,cached_workspace_bytes\n");
    foreach (var measurement in measurements)
    {
        var row = string.Join(',', measurement.Implementation, measurement.Phase,
            measurement.Files, measurement.RawBytes, measurement.CompressedBytes,
            measurement.ElapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture), measurement.AllocatedBytes,
            measurement.Gen0, measurement.Gen1, measurement.Gen2, measurement.ManagedBytesAfter, measurement.CachedWorkspaceBytes);
        Console.WriteLine(row);
        csv.AppendLine(row);
    }
    foreach (var implementation in implementations)
    {
        var warm = measurements.Where(value => value.Implementation == implementation.Name && value.Phase.StartsWith("warm-", StringComparison.Ordinal)).ToArray();
        Console.WriteLine(FormattableString.Invariant(
            $"WARM MEAN {implementation.Name}: {warm.Average(value => value.ElapsedMilliseconds):F2} ms, {warm.Average(value => value.AllocatedBytes) / 1048576d:F2} MiB allocated, GC counts {warm.Sum(value => value.Gen0)}/{warm.Sum(value => value.Gen1)}/{warm.Sum(value => value.Gen2)} across {repeats} passes."));
    }
    Console.WriteLine(FormattableString.Invariant($"Retained reusable workspace after benchmark: {RetainedWorkspaceBytes() / 1048576d:F2} MiB (private static byte[] fields, inspected only by this benchmark)."));
    if (output != null)
    {
        await File.WriteAllTextAsync(output, csv.ToString());
        Console.WriteLine($"CSV written to {Path.GetFullPath(output)}");
    }
    Console.WriteLine("These synthetic measurements do not establish in-game upload speed or prove the cause of a UI hitch.");
}
finally
{
    Directory.Delete(temporary, recursive: true);
}

static async Task<byte[]> BaselineAsync(string path, CancellationToken token)
{
    var bytes = await File.ReadAllBytesAsync(path, token);
    return LZ4Wrapper.WrapHC(bytes, 0, bytes.Length);
}

static async Task<Sample> CreateSampleAsync(string directory, string pattern, int length)
{
    var bytes = new byte[length];
    var random = new Random(0x5EED + length);
    if (pattern == "repeated")
    {
        for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index % 251);
    }
    else
    {
        random.NextBytes(bytes);
        if (pattern == "mixed")
        {
            const int tile = 16 * 1024;
            for (var block = 0; block + tile < bytes.Length; block += 4 * tile)
                for (var copy = 1; copy < 4 && block + copy * tile < bytes.Length; copy++)
                    bytes.AsSpan(block, Math.Min(tile, bytes.Length - block - copy * tile))
                        .CopyTo(bytes.AsSpan(block + copy * tile));
        }
    }
    var name = $"{pattern}-{length}.bin";
    var path = Path.Combine(directory, name);
    await File.WriteAllBytesAsync(path, bytes);
    return new Sample(name, path, length, SHA1.HashData(bytes));
}

static async Task<Measurement> MeasureAsync(string implementation, string phase, IReadOnlyList<Sample> files,
    Func<string, CancellationToken, Task<byte[]>> compress)
{
    // Intentionally isolated benchmark housekeeping, outside the measured interval.
    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    var gen0 = GC.CollectionCount(0);
    var gen1 = GC.CollectionCount(1);
    var gen2 = GC.CollectionCount(2);
    var allocated = GC.GetTotalAllocatedBytes(precise: true);
    var elapsed = Stopwatch.StartNew();
    long compressed = 0;
    foreach (var file in files)
    {
        var bytes = await compress(file.Path, CancellationToken.None);
        compressed += bytes.LongLength;
        GC.KeepAlive(bytes);
    }
    elapsed.Stop();
    var difference = GC.GetTotalAllocatedBytes(precise: true) - allocated;
    return new Measurement(implementation, phase, files.Count, files.Sum(file => (long)file.Length), compressed,
        elapsed.Elapsed.TotalMilliseconds, difference, GC.CollectionCount(0) - gen0,
        GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2, GC.GetTotalMemory(forceFullCollection: false), RetainedWorkspaceBytes());
}

static long RetainedWorkspaceBytes() => typeof(UploadFileCompressor)
    .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
    .Where(field => field.FieldType == typeof(byte[]))
    .Sum(field => (field.GetValue(null) as byte[])?.LongLength ?? 0);

sealed record Sample(string Name, string Path, int Length, byte[] Sha1);
sealed record Measurement(string Implementation, string Phase, int Files, long RawBytes,
    long CompressedBytes, double ElapsedMilliseconds, long AllocatedBytes, int Gen0, int Gen1, int Gen2,
    long ManagedBytesAfter, long CachedWorkspaceBytes);
