using StellarSync.WebAPI.Files.Models;

internal static class StreamContentTests
{
    public static async Task ContentLengthAndBytesAsync()
    {
        var expected = Enumerable.Range(0, 20_123).Select(index => (byte)(index * 31)).ToArray();
        using var content = new ProgressableStreamContent(new MemoryStream(expected), null);
        Check(content.Headers.ContentLength == expected.Length, "Upload content length does not match its source.");
        using var destination = new MemoryStream();
        await content.CopyToAsync(destination, CancellationToken.None);
        Check(destination.ToArray().SequenceEqual(expected), "Upload stream changed or truncated the file bytes.");
    }

    public static async Task ProgressFollowsWritesAsync()
    {
        var expected = new byte[12_345];
        using var destination = new GatedWriteStream();
        var reports = new List<UploadProgress>();
        var reportedBeforeWrite = false;
        var progress = new InlineProgress(report =>
        {
            if (report.Uploaded > destination.Length) reportedBeforeWrite = true;
            reports.Add(report);
        });
        using var content = new ProgressableStreamContent(new MemoryStream(expected), progress);
        var copy = content.CopyToAsync(destination, CancellationToken.None);
        try
        {
            await destination.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(reports.Count == 0, "Progress was reported before the first write completed.");
            Check(!copy.IsCompleted, "Upload completed while its first write was blocked.");
        }
        finally
        {
            destination.ReleaseWrites.TrySetResult();
            await copy.WaitAsync(TimeSpan.FromSeconds(3));
        }

        Check(!reportedBeforeWrite, "Progress advertised bytes that had not yet been written.");
        Check(reports.Count > 0, "Upload did not report progress.");
        var previous = 0L;
        foreach (var report in reports)
        {
            Check(report.Uploaded > previous, "Upload progress was not strictly increasing.");
            Check(report.Size == expected.Length, "Progress reported an incorrect file size.");
            Check(report.Uploaded <= expected.Length, "Progress exceeded the file size.");
            previous = report.Uploaded;
        }
        Check(previous == expected.Length, "Final progress did not include every uploaded byte.");
    }

    public static async Task CancellationInterruptsWriteAsync()
    {
        using var cancellation = new CancellationTokenSource();
        using var destination = new GatedWriteStream();
        using var content = new ProgressableStreamContent(new MemoryStream(new byte[8192]), null);
        var copy = content.CopyToAsync(destination, cancellation.Token);
        try
        {
            await destination.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            try
            {
                await copy.WaitAsync(TimeSpan.FromSeconds(3));
                throw new InvalidOperationException("Cancelled upload unexpectedly completed its blocked write.");
            }
            catch (OperationCanceledException)
            {
            }
            Check(destination.Length == 0, "Cancelled blocked write sent bytes after cancellation.");
        }
        finally
        {
            destination.ReleaseWrites.TrySetResult();
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class InlineProgress(Action<UploadProgress> report) : IProgress<UploadProgress>
    {
        public void Report(UploadProgress value) => report(value);
    }

    private sealed class GatedWriteStream : MemoryStream
    {
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrites { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            await ReleaseWrites.Task.WaitAsync(cancellationToken);
            await base.WriteAsync(buffer, cancellationToken);
        }
    }
}
