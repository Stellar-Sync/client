using StellarSync.WebAPI.Files;

internal static class LimiterTests
{
    public static async Task ConcurrentBatchesShareLimitAsync()
    {
        var limiter = new UploadConcurrencyLimiter();
        limiter.SetLimit(2);
        var full = Signal();
        var release = Signal();
        var active = 0;
        var peak = 0;
        var completed = 0;

        async Task UploadAsync(int file, byte[] bytes, CancellationToken token)
        {
            using var lease = await limiter.AcquireAsync(token);
            var current = Interlocked.Increment(ref active);
            Max(ref peak, current);
            if (current == 2) full.TrySetResult();
            try
            {
                Check(current <= 2, "Simultaneous batches exceeded the shared HTTP limit.");
                await release.Task.WaitAsync(token);
                Interlocked.Increment(ref completed);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }

        var files = Enumerable.Range(0, 4).ToArray();
        var first = UploadBatchRunner.RunAsync(files, 2,
            (_, _) => Task.FromResult(new byte[1]), UploadAsync, CancellationToken.None);
        var second = UploadBatchRunner.RunAsync(files, 2,
            (_, _) => Task.FromResult(new byte[1]), UploadAsync, CancellationToken.None);
        try
        {
            await full.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(30);
            Check(active == 2 && peak == 2, $"Expected two HTTP uploads across batches, got {active}/{peak}.");
            Check(!first.IsCompleted && !second.IsCompleted, "Batch completed despite blocked uploads.");
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        }
        Check(completed == 8 && active == 0, "Not all uploads completed and released their shared slots.");
    }

    public static async Task IncreasingLimitWakesAvailableWaitersAsync()
    {
        var limiter = new UploadConcurrencyLimiter();
        limiter.SetLimit(1);
        using var occupied = await limiter.AcquireAsync(CancellationToken.None);
        var waiters = Enumerable.Range(0, 12).Select(_ => limiter.AcquireAsync(CancellationToken.None)).ToList();
        Check(waiters.All(task => !task.IsCompleted), "A waiter acquired an already occupied slot.");
        limiter.SetLimit(10);
        var leases = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 9; index++)
            {
                var ready = await Task.WhenAny(waiters).WaitAsync(TimeSpan.FromSeconds(3));
                leases.Add(await ready);
                waiters.Remove(ready);
            }
            Check(waiters.Count == 3 && waiters.All(task => !task.IsCompleted),
                "Increasing the limit admitted more than ten simultaneous leases.");
        }
        finally
        {
            occupied.Dispose();
            foreach (var lease in leases) lease.Dispose();
            foreach (var lease in await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(3))) lease.Dispose();
        }
    }

    public static async Task ReducingLimitDrainsNaturallyAsync()
    {
        var limiter = new UploadConcurrencyLimiter();
        limiter.SetLimit(4);
        var active = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => limiter.AcquireAsync(CancellationToken.None)));
        limiter.SetLimit(1);
        var waiter = limiter.AcquireAsync(CancellationToken.None);
        try
        {
            Check(!waiter.IsCompleted, "Reduced limit admitted another upload while above capacity.");
            active[0].Dispose();
            active[1].Dispose();
            active[2].Dispose();
            await Task.Delay(30);
            Check(!waiter.IsCompleted, "Reduced limit admitted an upload while the remaining slot was occupied.");
            active[3].Dispose();
            using var lease = await waiter.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            foreach (var lease in active) lease.Dispose();
        }
    }

    public static async Task CancelledWaitDoesNotLeakAsync()
    {
        var limiter = new UploadConcurrencyLimiter();
        limiter.SetLimit(1);
        using var cancellation = new CancellationTokenSource();
        using var occupied = await limiter.AcquireAsync(CancellationToken.None);
        var cancelledWait = limiter.AcquireAsync(cancellation.Token);
        cancellation.Cancel();
        try
        {
            using var unexpected = await cancelledWait.WaitAsync(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Cancelled waiter unexpectedly acquired a slot.");
        }
        catch (OperationCanceledException)
        {
        }
        occupied.Dispose();
        using var next = await limiter.AcquireAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        var another = limiter.AcquireAsync(CancellationToken.None);
        Check(!another.IsCompleted, "Cancelled waiter caused the limiter to allow extra slots.");
        next.Dispose();
        using var final = await another.WaitAsync(TimeSpan.FromSeconds(3));
    }

    public static async Task LeaseReleaseIsIdempotentAsync()
    {
        var limiter = new UploadConcurrencyLimiter();
        limiter.SetLimit(1);
        using var first = await limiter.AcquireAsync(CancellationToken.None);
        first.Dispose();
        first.Dispose();
        using var second = await limiter.AcquireAsync(CancellationToken.None);
        var waiter = limiter.AcquireAsync(CancellationToken.None);
        Check(!waiter.IsCompleted, "Duplicate release increased available concurrency.");
        second.Dispose();
        using var third = await waiter.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Max(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (current < value)
        {
            var previous = Interlocked.CompareExchange(ref target, value, current);
            if (previous == current) return;
            current = previous;
        }
    }
}
