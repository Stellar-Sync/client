using System.Net;
using Microsoft.Extensions.Logging;
using StellarSync.API.Data;
using StellarSync.FileCache;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using StellarSync.StellarConfiguration;
using StellarSync.WebAPI.Files;

var tests = new (string Name, Func<Task> Run)[]
{
    ("manager honors one configured upload", () => ManagerConcurrencyAsync(1)),
    ("manager honors two configured uploads", () => ManagerConcurrencyAsync(2)),
    ("manager honors four configured uploads", () => ManagerConcurrencyAsync(4)),
    ("manager honors ten configured uploads", () => ManagerConcurrencyAsync(10)),
    ("manager clamps a zero legacy setting to one", () => ManagerConcurrencyAsync(0)),
    ("manager clamps a negative setting to one", () => ManagerConcurrencyAsync(-5)),
    ("manager clamps an excessive setting to ten", () => ManagerConcurrencyAsync(11)),
    ("manager clamps the largest integer setting to ten", () => ManagerConcurrencyAsync(int.MaxValue)),
    ("HTTP errors remain unverified and dispose responses", RejectedUploadRemainsUnverifiedAsync),
    ("successful fallback verifies the file and restores its bytes", FallbackVerifiesAndRestoresAsync),
    ("cancelled previous outfit cannot clear newer progress", PreviousOutfitCannotClearProgressAsync),
    ("disconnect invalidates late upload completion", DisconnectInvalidatesLateCompletionAsync),
    ("manual upload survives character upload cancellation", ManualUploadSurvivesCharacterCancellationAsync),
    ("all batches share one compression operation", CompressionIsGloballySerialAsync),
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {error}");
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} upload manager integration tests passed.");
return failures == 0 ? 0 : 1;

static async Task ManagerConcurrencyAsync(int limit)
{
    var hashes = Enumerable.Range(0, 12).Select(i => $"file{i}").ToArray();
    using var fixture = new Fixture(hashes);
    fixture.Config.Current.ParallelUploads = limit;
    var expected = Math.Clamp(limit, 1, 10);
    var reachedLimit = Signal();
    var release = Signal();
    int active = 0, peak = 0;
    fixture.Transport.Upload = async (_, _, token) =>
    {
        var current = Interlocked.Increment(ref active);
        peak = Math.Max(peak, current);
        if (current == expected) reachedLimit.TrySetResult();
        try
        {
            await release.Task.WaitAsync(token);
            return new TrackingResponse(HttpStatusCode.OK);
        }
        finally { Interlocked.Decrement(ref active); }
    };
    var upload = fixture.Manager.UploadFiles(Character(hashes), []);
    try
    {
        await reachedLimit.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(active == expected && peak == expected, "Manager did not apply the selected upload mode.");
        Check(!upload.IsCompleted, "Manager completed before its active uploads.");
    }
    finally { release.TrySetResult(); }
    await upload.WaitAsync(TimeSpan.FromSeconds(3));
    Check(active == 0 && peak == expected, "Manager exceeded its limit or leaked active uploads.");
}

static async Task RejectedUploadRemainsUnverifiedAsync()
{
    using var fixture = new Fixture("A");
    fixture.Transport.Upload = (_, _, _) => Task.FromResult<HttpResponseMessage>(new TrackingResponse(HttpStatusCode.RequestEntityTooLarge));
    Check(await CatchAsync(fixture.Manager.UploadFiles(Character("A"), [])) is HttpRequestException,
        "HTTP rejection was not propagated.");
    Check(fixture.Transport.Responses.All(response => response.Disposed), "An HTTP response was not disposed after rejection.");
    fixture.Transport.Upload = (_, _, _) => Task.FromResult<HttpResponseMessage>(new TrackingResponse(HttpStatusCode.OK));
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(fixture.Transport.Negotiations.Count == 2, "Rejected file was cached as successfully verified.");
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(fixture.Transport.Negotiations.Count == 2, "Successful file was not cached as verified.");
}

static async Task FallbackVerifiesAndRestoresAsync()
{
    using var fixture = new Fixture("A");
    var bytes = new byte[] { 4, 5, 6, 7 };
    fixture.Cache.Prepare = (hash, _) => Task.FromResult((hash, bytes));
    var attempts = new List<(string Path, byte[] Bytes)>();
    fixture.Transport.Upload = async (uri, content, token) =>
    {
        attempts.Add((uri.AbsolutePath, await content.ReadAsByteArrayAsync(token)));
        return new TrackingResponse(attempts.Count == 1 ? HttpStatusCode.BadRequest : HttpStatusCode.OK);
    };
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(attempts.Count == 2, "Expected one normal upload and one fallback.");
    Check(attempts[0].Path.Contains("/upload/", StringComparison.Ordinal), "First attempt used the wrong endpoint.");
    Check(attempts[1].Path.Contains("/upload-munged/", StringComparison.Ordinal), "Fallback used the wrong endpoint.");
    Check(attempts[0].Bytes.SequenceEqual(new byte[] { 4, 5, 6, 7 }), "Normal upload changed its content.");
    Check(attempts[1].Bytes.SequenceEqual(new byte[] { 46, 47, 44, 45 }), "Fallback did not send munged bytes.");
    Check(bytes.SequenceEqual(new byte[] { 4, 5, 6, 7 }), "Fallback left the source buffer munged.");
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(fixture.Transport.Negotiations.Count == 1, "Successful fallback did not verify the hash.");
    Check(fixture.Transport.Responses.All(response => response.Disposed), "A successful/fallback response leaked.");
}

static async Task PreviousOutfitCannotClearProgressAsync()
{
    using var fixture = new Fixture("A", "B");
    var oldStarted = Signal();
    var newStarted = Signal();
    var releaseOld = Signal();
    var releaseNew = Signal();
    fixture.Transport.Upload = async (uri, _, _) =>
    {
        if (uri.AbsolutePath.EndsWith("/A", StringComparison.Ordinal))
        {
            oldStarted.TrySetResult();
            await releaseOld.Task; // Deliberately emulate a response racing cancellation.
        }
        else
        {
            newStarted.TrySetResult();
            await releaseNew.Task;
        }
        return new TrackingResponse(HttpStatusCode.OK);
    };
    var oldUpload = fixture.Manager.UploadFiles(Character("A"), []);
    await oldStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var newUpload = fixture.Manager.UploadFiles(Character("B"), []);
    try
    {
        await newStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        releaseOld.TrySetResult();
        Check(await CatchAsync(oldUpload) is OperationCanceledException, "Superseded upload did not cancel.");
        Check(fixture.Manager.CurrentUploads.Single().Hash == "B", "Old session cleared or replaced newer progress.");
        Check(fixture.Manager.IsUploading, "Old session marked the newer upload inactive.");
    }
    finally
    {
        releaseOld.TrySetResult();
        releaseNew.TrySetResult();
        await newUpload.WaitAsync(TimeSpan.FromSeconds(3));
    }
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(fixture.Transport.Negotiations.Count == 3, "Late cancelled upload was incorrectly verified.");
}

static async Task DisconnectInvalidatesLateCompletionAsync()
{
    using var fixture = new Fixture("A");
    var started = Signal();
    var release = Signal();
    fixture.Transport.Upload = async (_, _, _) =>
    {
        started.TrySetResult();
        await release.Task;
        return new TrackingResponse(HttpStatusCode.OK);
    };
    var old = fixture.Manager.UploadFiles(Character("A"), []);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
    fixture.Mediator.Publish(new DisconnectedMessage());
    release.TrySetResult();
    Check(await CatchAsync(old) is OperationCanceledException, "Disconnected upload was allowed to complete successfully.");
    Check(!fixture.Manager.IsUploading && fixture.Manager.CurrentUploads.Count == 0, "Disconnect retained active character progress.");
    await fixture.Manager.UploadFiles(Character("A"), []);
    Check(fixture.Transport.Negotiations.Count == 2, "Disconnected session repopulated verification state.");
}

static async Task ManualUploadSurvivesCharacterCancellationAsync()
{
    using var fixture = new Fixture("A", "B");
    var manualStarted = Signal();
    var characterStarted = Signal();
    var releaseManual = Signal();
    var manualTokenCancelled = false;
    fixture.Transport.Upload = async (uri, _, token) =>
    {
        if (uri.AbsolutePath.EndsWith("/A", StringComparison.Ordinal))
        {
            manualStarted.TrySetResult();
            await releaseManual.Task;
            manualTokenCancelled = token.IsCancellationRequested;
        }
        else
        {
            characterStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        return new TrackingResponse(HttpStatusCode.OK);
    };
    var manual = fixture.Manager.UploadFiles(["A"], new InlineProgress());
    await manualStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var character = fixture.Manager.UploadFiles(Character("B"), []);
    try
    {
        await characterStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(fixture.Manager.CancelUpload(), "Character upload was not considered cancellable.");
        Check(await CatchAsync(character) is OperationCanceledException, "Character cancellation did not propagate.");
        Check(!manual.IsCompleted, "Character cancellation terminated the manual upload.");
    }
    finally
    {
        releaseManual.TrySetResult();
    }
    Check((await manual.WaitAsync(TimeSpan.FromSeconds(3))).Count == 0, "Manual upload did not complete successfully.");
    Check(!manualTokenCancelled, "Character cancellation cancelled the manual upload token.");
}

static async Task CompressionIsGloballySerialAsync()
{
    using var fixture = new Fixture("A", "B");
    var firstPreparing = Signal();
    var releaseFirst = Signal();
    var first = 0;
    var preparing = 0;
    var peak = 0;
    fixture.Cache.Prepare = async (hash, token) =>
    {
        var current = Interlocked.Increment(ref preparing);
        peak = Math.Max(peak, current);
        try
        {
            if (Interlocked.Increment(ref first) == 1)
            {
                firstPreparing.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
            }
            return (hash, new byte[4]);
        }
        finally
        {
            Interlocked.Decrement(ref preparing);
        }
    };
    var manual = fixture.Manager.UploadFiles(["A"], new InlineProgress());
    await firstPreparing.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var character = fixture.Manager.UploadFiles(Character("B"), []);
    try
    {
        await Task.Delay(30);
        Check(first == 1 && peak == 1, "Independent batches compressed files concurrently.");
    }
    finally
    {
        releaseFirst.TrySetResult();
    }
    await Task.WhenAll(manual, character).WaitAsync(TimeSpan.FromSeconds(3));
    Check(peak == 1, "Compression exceeded one worker across batches.");
}

static CharacterData Character(params string[] hashes)
{
    var data = new CharacterData();
    data.FileReplacements["files"] = hashes.Select(hash => new FileReplacement { Hash = hash }).ToList();
    return data;
}

static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task<Exception> CatchAsync(Task task)
{
    try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (Exception error) { return error; }
    throw new InvalidOperationException("Expected failure, but the upload completed successfully.");
}

sealed class InlineProgress : IProgress<string> { public void Report(string value) { } }

sealed class Fixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "stellar-upload-tests-" + Guid.NewGuid().ToString("N"));
    public FileCacheManager Cache { get; } = new();
    public FileTransferOrchestrator Transport { get; } = new();
    public StellarMediator Mediator { get; } = new();
    public StellarConfigService Config { get; } = new();
    public FileUploadManager Manager { get; }

    public Fixture(params string[] hashes)
    {
        Directory.CreateDirectory(_directory);
        foreach (var hash in hashes)
        {
            var path = Path.Combine(_directory, hash);
            File.WriteAllBytes(path, new byte[4]);
            Cache.Files[hash] = new FileCacheEntity { ResolvedFilepath = path };
        }
        Manager = new FileUploadManager(new TestLogger<FileUploadManager>(), Mediator, Config, Transport, Cache, new ServerConfigurationManager());
    }

    public void Dispose()
    {
        Manager.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}
