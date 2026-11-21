using StellarSync.WebAPI.Files;

// Exercise the production scheduler without loading Dalamud or making HTTP requests.
var tests = new (string Name, Func<Task> Run)[]
{
    ("one upload with one prepared file", () => CheckConcurrencyAsync(1)),
    ("two simultaneous uploads", () => CheckConcurrencyAsync(2)),
    ("four simultaneous uploads", () => CheckConcurrencyAsync(4)),
    ("ten simultaneous uploads", () => CheckConcurrencyAsync(10)),
    ("preparation remains serial", PreparationRemainsSerialAsync),
    ("upload failure cancels and drains sibling uploads", UploadFailureDrainsAsync),
    ("preparation failure cancels and drains uploads", PreparationFailureDrainsAsync),
    ("caller cancellation drains uploads", CancellationDrainsAsync),
    ("caller cancellation interrupts preparation", CancellationInterruptsPreparationAsync),
    ("pre-cancelled batch starts no work", PreCancelledStartsNoWorkAsync),
    ("empty batch starts no work", EmptyBatchStartsNoWorkAsync),
    ("simultaneous batches share the HTTP limit", LimiterTests.ConcurrentBatchesShareLimitAsync),
    ("increasing the limit wakes only available slots", LimiterTests.IncreasingLimitWakesAvailableWaitersAsync),
    ("reducing the limit lets active uploads finish", LimiterTests.ReducingLimitDrainsNaturallyAsync),
    ("cancelled limiter wait does not consume a slot", LimiterTests.CancelledWaitDoesNotLeakAsync),
    ("releasing a limiter lease twice is harmless", LimiterTests.LeaseReleaseIsIdempotentAsync),
    ("upload stream preserves content length and bytes", StreamContentTests.ContentLengthAndBytesAsync),
    ("upload progress reports only completed writes", StreamContentTests.ProgressFollowsWritesAsync),
    ("upload stream cancellation interrupts a blocked write", StreamContentTests.CancellationInterruptsWriteAsync),
};

var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} upload scheduler tests passed.");
return failures == 0 ? 0 : 1;

static async Task CheckConcurrencyAsync(int limit)
{
    var count = Math.Max(12, limit + 3);
    var expected = limit;
    var files = Enumerable.Range(0, count).ToArray();
    var started = Signal();
    var preparedAhead = Signal();
    var release = Signal();
    var active = 0;
    var peak = 0;
    var prepared = 0;
    var completed = 0;
    var released = 0;
    var preparedTooFarAhead = 0;

    var task = UploadBatchRunner.RunAsync(files, limit,
        async (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            var current = Interlocked.Increment(ref prepared);
            if (current > expected + 1 && Volatile.Read(ref released) == 0)
                Interlocked.Exchange(ref preparedTooFarAhead, 1);
            if (current == Math.Min(count, expected + 1)) preparedAhead.TrySetResult();
            await Task.Yield();
            return new byte[1];
        },
        async (_, _, token) =>
        {
            var current = Interlocked.Increment(ref active);
            Max(ref peak, current);
            if (current == expected) started.TrySetResult();
            try
            {
                await release.Task.WaitAsync(token);
                Interlocked.Increment(ref completed);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, CancellationToken.None);

    try
    {
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await preparedAhead.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!task.IsCompleted, "Batch completed while uploads were still blocked.");
        Check(Volatile.Read(ref active) == expected, $"Expected {expected} simultaneous uploads, got {active}.");
        // Hold the artificial network stall long enough for an incorrectly unbounded
        // producer to expose itself. The invariant is also checked inside preparation.
        await Task.Delay(30);
        Check(Volatile.Read(ref peak) == expected, $"Unexpected maximum concurrency {peak}.");
        Check(Volatile.Read(ref preparedTooFarAhead) == 0, "Producer accumulated more than one prepared file beyond active uploads.");
        Check(prepared <= Math.Min(count, expected + 1), $"Prepared {prepared} files while only {expected} could upload.");
    }
    finally
    {
        Interlocked.Exchange(ref released, 1);
        release.TrySetResult();
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    Check(completed == count, $"Only {completed}/{count} uploads completed.");
    Check(active == 0, "Uploads remain active after batch completion.");
}

static async Task PreparationRemainsSerialAsync()
{
    var preparing = 0;
    var peak = 0;
    var uploaded = 0;
    await UploadBatchRunner.RunAsync(Enumerable.Range(0, 20).ToArray(), 10,
        async (_, token) =>
        {
            Max(ref peak, Interlocked.Increment(ref preparing));
            try
            {
                await Task.Delay(1, token);
                return new byte[1];
            }
            finally
            {
                Interlocked.Decrement(ref preparing);
            }
        },
        async (_, _, token) =>
        {
            await Task.Delay(2, token);
            Interlocked.Increment(ref uploaded);
        }, CancellationToken.None);
    Check(peak == 1, $"Preparation ran {peak} operations concurrently.");
    Check(uploaded == 20, $"Only {uploaded}/20 uploads completed.");
}

static async Task UploadFailureDrainsAsync()
{
    var siblingStarted = Signal();
    var cancellationObserved = Signal();
    var releaseCleanup = Signal();
    var failure = new InvalidOperationException("simulated HTTP failure");
    var active = 0;

    var task = UploadBatchRunner.RunAsync(Enumerable.Range(0, 8).ToArray(), 2,
        (_, _) => Task.FromResult(new byte[1]),
        async (file, _, token) =>
        {
            Interlocked.Increment(ref active);
            try
            {
                if (file == 0)
                {
                    await siblingStarted.Task.WaitAsync(token);
                    throw failure;
                }

                siblingStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    cancellationObserved.TrySetResult();
                    await releaseCleanup.Task;
                    throw;
                }
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, CancellationToken.None);

    try
    {
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!task.IsCompleted, "Failure returned before sibling cleanup finished.");
    }
    finally
    {
        releaseCleanup.TrySetResult();
    }

    var observed = await CatchAsync(task);
    Check(ReferenceEquals(observed, failure), $"Expected original upload failure, got {observed.GetType().Name}.");
    Check(active == 0, "Upload failure left sibling work running.");
}

static async Task PreparationFailureDrainsAsync()
{
    var uploadStarted = Signal();
    var cancellationObserved = Signal();
    var releaseCleanup = Signal();
    var failure = new IOException("simulated file read failure");
    var active = 0;
    var uploads = 0;
    var task = UploadBatchRunner.RunAsync(Enumerable.Range(0, 4).ToArray(), 2,
        async (file, token) =>
        {
            if (file == 1)
            {
                await uploadStarted.Task.WaitAsync(token);
                throw failure;
            }
            return new byte[1];
        },
        async (_, _, token) =>
        {
            Interlocked.Increment(ref uploads);
            Interlocked.Increment(ref active);
            uploadStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                await releaseCleanup.Task;
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, CancellationToken.None);
    try
    {
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!task.IsCompleted, "Preparation failure returned before upload cleanup finished.");
    }
    finally
    {
        releaseCleanup.TrySetResult();
    }
    var observed = await CatchAsync(task);
    Check(ReferenceEquals(observed, failure), $"Expected original preparation failure, got {observed.GetType().Name}.");
    Check(uploads == 1, $"Started {uploads} uploads after preparation failed.");
    Check(active == 0, "Preparation failure left an upload running.");
}

static async Task CancellationDrainsAsync()
{
    using var cancellation = new CancellationTokenSource();
    var started = Signal();
    var cancellationObserved = Signal();
    var releaseCleanup = Signal();
    var active = 0;
    var task = UploadBatchRunner.RunAsync(Enumerable.Range(0, 5).ToArray(), 1,
        (_, _) => Task.FromResult(new byte[1]),
        async (_, _, token) =>
        {
            Interlocked.Increment(ref active);
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                cancellationObserved.TrySetResult();
                await releaseCleanup.Task;
                throw;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, cancellation.Token);
    try
    {
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(!task.IsCompleted, "Cancellation returned before upload cleanup finished.");
    }
    finally
    {
        cancellation.Cancel();
        releaseCleanup.TrySetResult();
    }
    Check(await CatchAsync(task) is OperationCanceledException, "Caller cancellation did not propagate.");
    Check(active == 0, "Caller cancellation left an upload running.");
}

static async Task CancellationInterruptsPreparationAsync()
{
    using var cancellation = new CancellationTokenSource();
    var preparationStarted = Signal();
    var prepareCancelled = false;
    var uploads = 0;
    var task = UploadBatchRunner.RunAsync(new[] { 0, 1, 2 }, 1,
        async (file, token) =>
        {
            if (file == 1)
            {
                preparationStarted.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    prepareCancelled = true;
                    throw;
                }
            }
            return new byte[1];
        },
        async (_, _, token) =>
        {
            Interlocked.Increment(ref uploads);
            await Task.Delay(Timeout.Infinite, token);
        }, cancellation.Token);
    await preparationStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    cancellation.Cancel();
    Check(await CatchAsync(task) is OperationCanceledException, "Preparation cancellation did not propagate.");
    Check(prepareCancelled, "Preparation did not receive cancellation.");
    Check(uploads == 1, "A file was uploaded after its preparation was cancelled.");
}

static async Task PreCancelledStartsNoWorkAsync()
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    var calls = 0;
    var task = UploadBatchRunner.RunAsync(new[] { 1, 2 }, 2,
        (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new byte[1]);
        },
        (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, cancellation.Token);
    Check(await CatchAsync(task) is OperationCanceledException, "Pre-cancelled batch did not propagate cancellation.");
    Check(calls == 0, "Pre-cancelled batch performed work.");
}

static async Task EmptyBatchStartsNoWorkAsync()
{
    var calls = 0;
    await UploadBatchRunner.RunAsync(Array.Empty<int>(), 10,
        (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new byte[1]);
        },
        (_, _, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        }, CancellationToken.None);
    Check(calls == 0, "Empty batch performed work.");
}

static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Max(ref int target, int value)
{
    var current = Volatile.Read(ref target);
    while (current < value)
    {
        var previous = Interlocked.CompareExchange(ref target, value, current);
        if (previous == current) return;
        current = previous;
    }
}

static async Task<Exception> CatchAsync(Task task)
{
    try
    {
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    catch (Exception exception)
    {
        return exception;
    }
    throw new InvalidOperationException("Expected the batch to fail, but it completed successfully.");
}
