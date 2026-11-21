using K4os.Compression.LZ4.Legacy;
using StellarSync.FileCache;
using System.Security.Cryptography;

using var fixture = new CompressionFixture();
var tests = new List<(string Name, Func<Task> Run)>();
foreach (var (name, input) in CompatibilityInputs())
{
    tests.Add(($"legacy compatibility: {name}", async () =>
    {
        var path = await fixture.WriteAsync(input);
        var actual = await UploadFileCompressor.CompressAsync(path, CancellationToken.None);
        CheckCompatible(input, actual);
    }));
}

tests.Add(("previous results survive pooled-buffer reuse", RetainedOutputsRemainUnchangedAsync));
tests.Add(("concurrent calls preserve independent outputs", ConcurrentCallsRemainIndependentAsync));
tests.Add(("pre-cancelled compression propagates cancellation", PreCancelledCompressionAsync));
tests.Add(("cancellation does not poison the next call", CancellationDoesNotPoisonNextCallAsync));
tests.Add(("missing file propagates its error", MissingFileAsync));
tests.Add(("short stream reads are accumulated", ShortReadsAsync));
tests.Add(("stream compression respects the declared length", DeclaredLengthAsync));
tests.Add(("premature end of stream is rejected", PrematureEndOfStreamAsync));
tests.Add(("cancellation interrupts a partially completed read", CancellationDuringReadAsync));
tests.Add(("read failure does not poison the next compression", ReadFailureRecoveryAsync));

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(20));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{tests.Count - failures}/{tests.Count} compression tests passed.");
return failures == 0 ? 0 : 1;

async Task RetainedOutputsRemainUnchangedAsync()
{
    var retained = new List<(byte[] Input, byte[] Output, byte[] Snapshot)>();
    for (var index = 0; index < 18; index++)
    {
        // Alternate compressible and raw-fallback inputs in reused pool size buckets.
        var length = 70_000 + index * 139;
        var input = index % 2 == 0 ? RandomBytes(length, index + 200) : RepeatedBytes(length);
        var path = await fixture.WriteAsync(input);
        var output = await UploadFileCompressor.CompressAsync(path, CancellationToken.None);
        retained.Add((input, output, output.ToArray()));
    }

    foreach (var (input, output, snapshot) in retained)
    {
        Check(output.AsSpan().SequenceEqual(snapshot), "A returned result changed after another compression reused pooled buffers.");
        CheckCompatible(input, output);
    }
}

async Task ConcurrentCallsRemainIndependentAsync()
{
    var inputs = Enumerable.Range(0, 12)
        .Select(index => index % 3 == 0 ? RepeatedBytes(262_144 + index * 17) : RandomBytes(262_144 + index * 17, index + 500))
        .ToArray();
    var paths = new string[inputs.Length];
    for (var index = 0; index < inputs.Length; index++) paths[index] = await fixture.WriteAsync(inputs[index]);

    var outputs = await Task.WhenAll(paths.Select(path => UploadFileCompressor.CompressAsync(path, CancellationToken.None)));
    for (var index = 0; index < inputs.Length; index++) CheckCompatible(inputs[index], outputs[index]);
    for (var first = 0; first < outputs.Length; first++)
    {
        for (var second = first + 1; second < outputs.Length; second++)
            Check(!ReferenceEquals(outputs[first], outputs[second]), "Two non-empty files share the same returned array.");
    }
}

async Task PreCancelledCompressionAsync()
{
    var path = await fixture.WriteAsync(RandomBytes(65_537, 701));
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    try
    {
        await UploadFileCompressor.CompressAsync(path, cancellation.Token);
        throw new InvalidOperationException("A pre-cancelled compression unexpectedly succeeded.");
    }
    catch (OperationCanceledException)
    {
    }
}

async Task CancellationDoesNotPoisonNextCallAsync()
{
    await PreCancelledCompressionAsync();
    var input = RandomBytes(131_073, 702);
    var path = await fixture.WriteAsync(input);
    CheckCompatible(input, await UploadFileCompressor.CompressAsync(path, CancellationToken.None));
}

async Task MissingFileAsync()
{
    try
    {
        await UploadFileCompressor.CompressAsync(fixture.MissingPath, CancellationToken.None);
        throw new InvalidOperationException("Compressing a missing file unexpectedly succeeded.");
    }
    catch (FileNotFoundException)
    {
    }
}

async Task ShortReadsAsync()
{
    var input = RandomBytes(85_001, 800);
    using var source = new ChunkedReadStream(input, 37);
    CheckCompatible(input, await UploadFileCompressor.CompressAsync(source, input.Length, CancellationToken.None));
}

async Task DeclaredLengthAsync()
{
    var input = RandomBytes(70_111, 801);
    var sourceBytes = input.Concat(RandomBytes(8_192, 802)).ToArray();
    using var source = new MemoryStream(sourceBytes);
    CheckCompatible(input, await UploadFileCompressor.CompressAsync(source, input.Length, CancellationToken.None));
    Check(source.Position == input.Length, "Compression read past its declared file length.");
}

async Task PrematureEndOfStreamAsync()
{
    using var source = new ChunkedReadStream(RandomBytes(1_019, 803), 31);
    try
    {
        await UploadFileCompressor.CompressAsync(source, 8_192, CancellationToken.None);
        throw new InvalidOperationException("A truncated source was compressed as a complete file.");
    }
    catch (EndOfStreamException)
    {
    }
}

async Task CancellationDuringReadAsync()
{
    using var cancellation = new CancellationTokenSource();
    using var source = new PartialThenBlockedStream(RandomBytes(131_073, 804));
    var compression = UploadFileCompressor.CompressAsync(source, 131_073, cancellation.Token);
    await source.ReadBlocked.Task.WaitAsync(TimeSpan.FromSeconds(3));
    cancellation.Cancel();
    try
    {
        await compression.WaitAsync(TimeSpan.FromSeconds(3));
        throw new InvalidOperationException("Compression ignored cancellation during a blocked read.");
    }
    catch (OperationCanceledException)
    {
    }

    // Reuse the same retained-buffer size bucket after abandoning a partially filled buffer.
    var next = RandomBytes(131_071, 805);
    var path = await fixture.WriteAsync(next);
    CheckCompatible(next, await UploadFileCompressor.CompressAsync(path, CancellationToken.None));
}

async Task ReadFailureRecoveryAsync()
{
    var failure = new IOException("simulated source read failure");
    using var source = new FailingReadStream(RandomBytes(85_001, 806), failure);
    try
    {
        await UploadFileCompressor.CompressAsync(source, 85_001, CancellationToken.None);
        throw new InvalidOperationException("Compression ignored the source read failure.");
    }
    catch (IOException exception) when (ReferenceEquals(exception, failure))
    {
    }

    var input = RepeatedBytes(85_000);
    var path = await fixture.WriteAsync(input);
    CheckCompatible(input, await UploadFileCompressor.CompressAsync(path, CancellationToken.None));
}

static IEnumerable<(string Name, byte[] Input)> CompatibilityInputs()
{
    yield return ("empty", []);
    foreach (var length in new[] { 1, 7, 8, 9, 15, 16, 17, 31, 32, 255, 256, 4_095, 4_096, 4_097,
        65_535, 65_536, 65_537, 84_999, 85_000, 85_001, 131_071, 131_072, 131_073 })
        yield return ($"random {length} bytes", RandomBytes(length, length + 123));

    foreach (var length in new[] { 16, 128, 4_096, 65_536, 1_048_576 })
        yield return ($"repetitive {length} bytes", RepeatedBytes(length));

    foreach (var length in new[] { 65_535, 65_536, 65_537, 131_073, 1_048_577 })
    {
        var input = RandomBytes(length, length + 456);
        for (var offset = 0; offset < input.Length; offset += 2_048)
            input.AsSpan(offset, Math.Min(1_024, input.Length - offset)).Fill(0x5a);
        yield return ($"mixed {length} bytes", input);
    }

    yield return ("one MiB of zeroes", new byte[1_048_576]);
    yield return ("every byte value repeated", Enumerable.Range(0, 16_384).Select(index => (byte)index).ToArray());
}

static byte[] RandomBytes(int length, int seed)
{
    var bytes = new byte[length];
    new Random(seed).NextBytes(bytes);
    return bytes;
}

static byte[] RepeatedBytes(int length)
{
    var bytes = new byte[length];
    for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(index % 13);
    return bytes;
}

static void CheckCompatible(byte[] input, byte[] actual)
{
    var expected = LZ4Wrapper.WrapHC(input, 0, input.Length);
    Check(actual.Length == expected.Length, $"Legacy output length changed: expected {expected.Length}, got {actual.Length}.");
    Check(actual.AsSpan().SequenceEqual(expected), "Compressed bytes differ from LZ4Wrapper.WrapHC.");
    var unwrapped = LZ4Wrapper.Unwrap(actual);
    Check(unwrapped.AsSpan().SequenceEqual(input), "Legacy decoder did not recover the exact input bytes.");
    Check(SHA1.HashData(unwrapped).AsSpan().SequenceEqual(SHA1.HashData(input)), "Server-side SHA1 validation would reject this file.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

internal sealed class CompressionFixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "stellar-compression-tests-" + Guid.NewGuid().ToString("N"));
    private int _index;
    public string MissingPath => Path.Combine(_directory, "missing.bin");

    public CompressionFixture() => Directory.CreateDirectory(_directory);

    public async Task<string> WriteAsync(byte[] bytes)
    {
        var path = Path.Combine(_directory, Interlocked.Increment(ref _index) + ".bin");
        await File.WriteAllBytesAsync(path, bytes);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}

internal sealed class ChunkedReadStream(byte[] bytes, int chunkSize) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
}

internal sealed class PartialThenBlockedStream(byte[] bytes) : MemoryStream(bytes)
{
    public TaskCompletionSource ReadBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position == 0) return await base.ReadAsync(buffer[..Math.Min(41, buffer.Length)], cancellationToken);
        ReadBlocked.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException("Blocked read was released without cancellation.");
    }
}

internal sealed class FailingReadStream(byte[] bytes, IOException failure) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Position == 0) return base.ReadAsync(buffer[..Math.Min(41, buffer.Length)], cancellationToken);
        return ValueTask.FromException<int>(failure);
    }
}
