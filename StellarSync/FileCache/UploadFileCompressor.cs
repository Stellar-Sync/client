using K4os.Compression.LZ4;
using System.Buffers.Binary;

namespace StellarSync.FileCache;

// Matches LZ4Wrapper.WrapHC's legacy framing and L09_HC settings. Only temporary
// buffers are reused; the returned payload owns its bytes until upload completes.
internal static class UploadFileCompressor
{
    private const int HeaderLength = 8;
    private const int MaxRetainedBufferLength = 64 * 1024 * 1024;
    private static byte[]? _inputBuffer;
    private static byte[]? _scratchBuffer;

    public static async Task<byte[]> CompressAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await using var source = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            BufferSize = 1,
        });
        if (source.Length > Array.MaxLength - HeaderLength)
            throw new IOException("File is too large for the legacy upload format.");
        return await CompressAsync(source, (int)source.Length, token).ConfigureAwait(false);
    }

    internal static async Task<byte[]> CompressAsync(Stream source, int length, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (length > Array.MaxLength - HeaderLength)
            throw new ArgumentOutOfRangeException(nameof(length));
        token.ThrowIfCancellationRequested();
        if (length == 0) return new byte[HeaderLength];

        var input = Rent(ref _inputBuffer, length);
        byte[]? scratch = null;
        try
        {
            await source.ReadExactlyAsync(input.AsMemory(0, length), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            scratch = Rent(ref _scratchBuffer, length);

            // Use exactly the raw length as capacity, even when the cached buffer
            // is larger. This preserves the old wrapper's incompressible fallback.
            var encoded = LZ4Codec.Encode(input, 0, length, scratch, 0, length, LZ4Level.L09_HC);
            token.ThrowIfCancellationRequested();
            var compressed = encoded > 0 && encoded < length;
            var payloadLength = compressed ? encoded : length;
            var result = GC.AllocateUninitializedArray<byte>(HeaderLength + payloadLength);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(0, 4), length);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4, 4), payloadLength);
            (compressed ? scratch : input).AsSpan(0, payloadLength).CopyTo(result.AsSpan(HeaderLength));
            return result;
        }
        finally
        {
            Return(ref _inputBuffer, input);
            if (scratch != null) Return(ref _scratchBuffer, scratch);
        }
    }

    private static byte[] Rent(ref byte[]? cache, int minimumLength)
    {
        var buffer = Interlocked.Exchange(ref cache, null);
        if (buffer != null && buffer.Length >= minimumLength) return buffer;
        return GC.AllocateUninitializedArray<byte>(minimumLength);
    }

    private static void Return(ref byte[]? cache, byte[] buffer)
    {
        // One buffer per role, at most 128 MiB retained in total. Larger files
        // still work but their temporary arrays are not cached. Concurrent calls
        // can rent distinct buffers without returning memory still in use.
        if (buffer.Length > MaxRetainedBufferLength) return;
        while (true)
        {
            var current = Volatile.Read(ref cache);
            if (current != null && current.Length >= buffer.Length) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref cache, buffer, current), current)) return;
        }
    }
}
