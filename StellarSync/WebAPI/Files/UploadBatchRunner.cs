using System.Runtime.ExceptionServices;

namespace StellarSync.WebAPI.Files;

internal static class UploadBatchRunner
{
    // Preparation stays serial. At most one prepared file waits for a transfer slot.
    public static async Task RunAsync<T>(IReadOnlyList<T> files, int maxConcurrentUploads,
        Func<T, CancellationToken, Task<byte[]>> prepare,
        Func<T, byte[], CancellationToken, Task> upload, CancellationToken token)
    {
        if (maxConcurrentUploads is <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrentUploads));
        token.ThrowIfCancellationRequested();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = new List<Task>();
        ExceptionDispatchInfo? failure = null;

        async Task UploadAsync(T file, byte[] bytes)
        {
            try
            {
                await upload(file, bytes, cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
                await cancellation.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }

        async Task ObserveCompletedAsync()
        {
            foreach (var task in pending.Where(t => t.IsCompleted).ToArray())
            {
                await task.ConfigureAwait(false);
                pending.Remove(task);
            }
        }

        try
        {
            foreach (var file in files)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await ObserveCompletedAsync().ConfigureAwait(false);
                var bytes = await prepare(file, cancellation.Token).ConfigureAwait(false);
                while (pending.Count >= maxConcurrentUploads)
                {
                    await Task.WhenAny(pending).ConfigureAwait(false);
                    await ObserveCompletedAsync().ConfigureAwait(false);
                }

                cancellation.Token.ThrowIfCancellationRequested();
                pending.Add(UploadAsync(file, bytes));
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(ex), null);
            await cancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(pending).ConfigureAwait(false);
            }
            catch
            {
                // All started tasks must finish before the batch owner disposes its state.
            }

            failure?.Throw();
            throw;
        }
    }
}

// Shared across character and CharaData batches so their combined HTTP requests
// obey the selected limit. Changing the limit never cancels an active request.
internal sealed class UploadConcurrencyLimiter
{
    private readonly object _lock = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _limit = 2;
    private int _active;

    public void SetLimit(int limit)
    {
        if (limit is <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_lock)
        {
            _limit = limit;
            Signal();
        }
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken token)
    {
        while (true)
        {
            Task changed;
            lock (_lock)
            {
                token.ThrowIfCancellationRequested();
                if (_active < _limit)
                {
                    _active++;
                    return new Lease(this);
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private void Signal()
    {
        var previous = _changed;
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }

    private sealed class Lease(UploadConcurrencyLimiter owner) : IDisposable
    {
        private UploadConcurrencyLimiter? _owner = owner;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _owner, null);
            if (current == null) return;
            lock (current._lock)
            {
                current._active--;
                current.Signal();
            }
        }
    }
}
