using StellarSync.API.Data;
using StellarSync.API.Dto.Files;
using StellarSync.API.Routes;
using StellarSync.FileCache;
using StellarSync.StellarConfiguration;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using StellarSync.UI;
using StellarSync.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace StellarSync.WebAPI.Files;

public sealed class FileUploadManager : DisposableMediatorSubscriberBase
{
    private readonly FileCacheManager _fileDbManager;
    private readonly StellarConfigService _stellarConfigService;
    private readonly FileTransferOrchestrator _orchestrator;
    private readonly ServerConfigurationManager _serverManager;
    private readonly object _stateLock = new();
    private readonly Dictionary<string, DateTime> _verifiedUploadedHashes = new(StringComparer.Ordinal);
    private readonly HashSet<UploadSession> _sessions = [];
    private readonly SemaphoreSlim _compressionLock = new(1, 1);
    private readonly UploadConcurrencyLimiter _uploadSlots = new();
    private UploadSession? _characterSession;
    private FileTransfer[] _currentUploads = [];
    private long _connectionGeneration;
    private bool _disposed;

    public FileUploadManager(ILogger<FileUploadManager> logger, StellarMediator mediator,
        StellarConfigService stellarConfigService, FileTransferOrchestrator orchestrator,
        FileCacheManager fileDbManager, ServerConfigurationManager serverManager) : base(logger, mediator)
    {
        _stellarConfigService = stellarConfigService;
        _orchestrator = orchestrator;
        _fileDbManager = fileDbManager;
        _serverManager = serverManager;
        Mediator.Subscribe<DisconnectedMessage>(this, _ => Reset());
    }

    // Readers receive a snapshot; canceled workers retain only their own transfer objects.
    public IReadOnlyList<FileTransfer> CurrentUploads
    {
        get { lock (_stateLock) return _currentUploads.ToArray(); }
    }

    public bool IsUploading
    {
        get { lock (_stateLock) return _characterSession != null; }
    }

    public bool CancelUpload()
    {
        UploadSession? previous;
        lock (_stateLock)
        {
            previous = _characterSession;
            _characterSession = null;
            _currentUploads = [];
        }

        if (previous == null) return false;
        Logger.LogDebug("Cancelling current upload");
        previous.Cancel();
        return true;
    }

    public async Task DeleteAllFiles()
    {
        if (!_orchestrator.IsInitialized) throw new InvalidOperationException("FileTransferManager is not initialized");
        var uri = StellarFiles.ServerFilesDeleteAllFullPath(_orchestrator.FilesCdnUri!);
        Reset();
        using var response = await _orchestrator.SendRequestAsync(HttpMethod.Post,
            uri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        lock (_stateLock) _verifiedUploadedHashes.Clear();
    }

    public async Task<List<string>> UploadFiles(List<string> hashesToUpload, IProgress<string> progress, CancellationToken? ct = null)
    {
        // CharaData uploads have their own caller cancellation, independent of outfit changes.
        var session = BeginSession(character: false, ct ?? CancellationToken.None);
        try
        {
            var local = hashesToUpload.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
            var missing = hashesToUpload.Except(local, StringComparer.Ordinal).ToList();
            if (missing.Count != 0) return missing;
            progress.Report($"Starting upload for {local.Count} files");
            var files = await FilesSend(session, [.. local], []).ConfigureAwait(false);
            if (files.Exists(f => f.IsForbidden)) return [.. files.Where(f => f.IsForbidden).Select(f => f.Hash)];
            var transfers = files.DistinctBy(f => f.Hash).Select(CreateTransfer).ToArray();
            int started = 0;
            await RunUploads(session, transfers, () => progress.Report(
                $"Uploading file {Interlocked.Increment(ref started)}/{transfers.Length}. Please wait until the upload is completed.")).ConfigureAwait(false);
            return [];
        }
        finally
        {
            EndSession(session);
        }
    }

    public async Task<CharacterData> UploadFiles(CharacterData data, List<UserData> visiblePlayers)
    {
        var session = BeginSession(character: true, CancellationToken.None);
        try
        {
            Logger.LogDebug("Sending Character data {hash} to service {url}", data.DataHash.Value, _serverManager.CurrentApiUrl);
            var unverified = GetUnverifiedFiles(data);
            if (unverified.Count != 0)
            {
                await UploadUnverifiedFiles(session, unverified, visiblePlayers).ConfigureAwait(false);
                ValidateSession(session);
                Logger.LogInformation("Upload complete for {hash}", data.DataHash.Value);
            }

            var forbidden = _orchestrator.ForbiddenTransfers;
            foreach (var replacements in data.FileReplacements.Values)
                replacements.RemoveAll(i => forbidden.Exists(f => string.Equals(f.Hash, i.Hash, StringComparison.OrdinalIgnoreCase)));
            ValidateSession(session);
            return data;
        }
        finally
        {
            EndSession(session);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_stateLock) _disposed = true;
            Reset();
        }
        base.Dispose(disposing);
    }

    private UploadSession BeginSession(bool character, CancellationToken token)
    {
        UploadSession? previous;
        UploadSession session;
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var uri = _orchestrator.FilesCdnUri ?? throw new InvalidOperationException("FileTransferManager is not initialized");
            int limit = Math.Clamp(_stellarConfigService.Current.ParallelUploads, 1, 10);
            session = new UploadSession(uri, _connectionGeneration, character, limit,
                _stellarConfigService.Current.UseAlternativeFileUpload, token);
            _sessions.Add(session);
            previous = character ? _characterSession : null;
            if (character)
            {
                _characterSession = session;
                _currentUploads = [];
            }
            _uploadSlots.SetLimit(limit);
        }
        previous?.Cancel();
        return session;
    }

    private void EndSession(UploadSession session)
    {
        lock (_stateLock)
        {
            _sessions.Remove(session);
            if (ReferenceEquals(_characterSession, session))
            {
                _characterSession = null;
                _currentUploads = [];
            }
        }
        session.Dispose();
    }

    private void ValidateSession(UploadSession session)
    {
        lock (_stateLock)
        {
            session.Token.ThrowIfCancellationRequested();
            if (session.Generation != _connectionGeneration || !_sessions.Contains(session)
                || (session.Character && !ReferenceEquals(_characterSession, session)))
                throw new OperationCanceledException("Upload session was superseded or disconnected.", session.Token);
        }
    }

    private void MarkVerified(UploadSession session, string hash)
    {
        lock (_stateLock)
        {
            ValidateSession(session);
            _verifiedUploadedHashes[hash] = DateTime.UtcNow;
        }
    }

    private async Task<List<UploadFileDto>> FilesSend(UploadSession session, List<string> hashes, List<string> uids)
    {
        ValidateSession(session);
        using var response = await _orchestrator.SendRequestAsync(HttpMethod.Post,
            StellarFiles.ServerFilesFilesSendFullPath(session.CdnUri),
            new FilesSendDto { FileHashes = hashes, UIDs = uids }, session.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ValidateSession(session);
        return await response.Content.ReadFromJsonAsync<List<UploadFileDto>>(cancellationToken: session.Token).ConfigureAwait(false) ?? [];
    }

    private HashSet<string> GetUnverifiedFiles(CharacterData data)
    {
        var hashes = data.FileReplacements.Values.SelectMany(v => v)
            .Where(f => string.IsNullOrEmpty(f.FileSwapPath)).Select(f => f.Hash).Distinct(StringComparer.Ordinal);
        lock (_stateLock)
        {
            var threshold = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(10));
            return hashes.Where(hash => !_verifiedUploadedHashes.TryGetValue(hash, out var verified) || verified < threshold)
                .ToHashSet(StringComparer.Ordinal);
        }
    }

    private void Reset()
    {
        UploadSession[] sessions;
        lock (_stateLock)
        {
            _connectionGeneration++;
            _verifiedUploadedHashes.Clear();
            _currentUploads = [];
            _characterSession = null;
            sessions = [.. _sessions];
        }
        foreach (var session in sessions) session.Cancel();
    }

    private UploadFileTransfer CreateTransfer(UploadFileDto file) => new(file)
    {
        Total = new FileInfo((_fileDbManager.GetFileCacheByHash(file.Hash)
            ?? throw new FileNotFoundException($"Upload file {file.Hash} is no longer available.")).ResolvedFilepath).Length,
    };

    private async Task UploadUnverifiedFiles(UploadSession session, HashSet<string> unverified, List<UserData> visiblePlayers)
    {
        unverified = unverified.Where(h => _fileDbManager.GetFileCacheByHash(h) != null).ToHashSet(StringComparer.Ordinal);
        var files = await FilesSend(session, [.. unverified], visiblePlayers.Select(p => p.UID).ToList()).ConfigureAwait(false);
        var transfers = files.Where(f => !f.IsForbidden).DistinctBy(f => f.Hash).Select(CreateTransfer).ToArray();
        lock (_stateLock)
        {
            ValidateSession(session);
            _currentUploads = transfers;
        }
        foreach (var file in files.Where(f => f.IsForbidden))
        {
            ValidateSession(session);
            _orchestrator.AddForbiddenTransfer(new UploadFileTransfer(file)
            {
                LocalFile = _fileDbManager.GetFileCacheByHash(file.Hash)?.ResolvedFilepath ?? string.Empty,
            });
            MarkVerified(session, file.Hash);
        }
        var toUpload = files.Select(f => f.Hash).ToHashSet(StringComparer.Ordinal);
        foreach (var hash in unverified.Where(h => !toUpload.Contains(h))) MarkVerified(session, hash);
        await RunUploads(session, transfers).ConfigureAwait(false);
    }

    private async Task RunUploads(UploadSession session, UploadFileTransfer[] transfers, Action? onStart = null)
    {
        var elapsed = Stopwatch.StartNew();
        long completedBytes = 0;
        long compressionTicks = 0;
        int completed = 0;
        int active = 0;
        int peak = 0;
        UploadDiagnostics? diagnostics = transfers.Length == 0 ? null : UploadDiagnostics.Capture();
        Logger.LogInformation("Upload batch starting: {count} files, concurrency {limit}", transfers.Length,
            session.Limit);
        try
        {
            await UploadBatchRunner.RunAsync(transfers, session.Limit, async (transfer, token) =>
            {
                await _compressionLock.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    ValidateSession(session);
                    Logger.LogDebug("[{hash}] Compressing", transfer.Hash);
                    var compression = Stopwatch.StartNew();
                    var data = await _fileDbManager.GetCompressedFileData(transfer.Hash, token).ConfigureAwait(false);
                    compressionTicks += compression.Elapsed.Ticks;
                    token.ThrowIfCancellationRequested();
                    transfer.Total = data.Item2.LongLength;
                    return data.Item2;
                }
                finally { _compressionLock.Release(); }
            }, async (transfer, bytes, token) =>
            {
                using var slot = await _uploadSlots.AcquireAsync(token).ConfigureAwait(false);
                ValidateSession(session);
                var current = Interlocked.Increment(ref active);
                int previous;
                do { previous = Volatile.Read(ref peak); }
                while (current > previous && Interlocked.CompareExchange(ref peak, current, previous) != previous);
                try
                {
                    onStart?.Invoke();
                    await UploadFile(session, transfer, bytes, token).ConfigureAwait(false);
                    Interlocked.Add(ref completedBytes, bytes.LongLength);
                    Interlocked.Increment(ref completed);
                }
                finally { Interlocked.Decrement(ref active); }
            }, session.Token).ConfigureAwait(false);
            ValidateSession(session);
            Logger.LogInformation("Upload batch complete: {count} files, {bytes} compressed bytes in {seconds:F2}s ({rate:F2} MiB/s), concurrency {limit}, peak active uploads {peak}, compression {compression:F2}s",
                completed, completedBytes, elapsed.Elapsed.TotalSeconds,
                completedBytes / 1048576d / Math.Max(elapsed.Elapsed.TotalSeconds, 0.001),
                session.Limit, peak, TimeSpan.FromTicks(compressionTicks).TotalSeconds);
        }
        finally
        {
            diagnostics?.LogChanges(Logger);
        }
    }

    private async Task UploadFile(UploadSession session, UploadFileTransfer transfer, byte[] bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Logger.LogInformation("[{hash}] Uploading {size}", transfer.Hash, UiSharedService.ByteToString(bytes.Length));
        try
        {
            await UploadFileStream(session, transfer, bytes, session.Alternative, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!session.Alternative && ex is not OperationCanceledException && !token.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "[{hash}] Error during file upload, trying alternative file upload", transfer.Hash);
            transfer.Transferred = 0;
            await UploadFileStream(session, transfer, bytes, munged: true, token).ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
        MarkVerified(session, transfer.Hash);
        transfer.Transferred = transfer.Total;
    }

    private async Task UploadFileStream(UploadSession session, UploadFileTransfer transfer, byte[] bytes, bool munged, CancellationToken token)
    {
        if (munged) FileDownloadManager.MungeBuffer(bytes.AsSpan());
        try
        {
            using var ms = new MemoryStream(bytes);
            using var content = new ProgressableStreamContent(ms, new TransferProgress(transfer));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            var uri = munged ? StellarFiles.ServerFilesUploadMunged(session.CdnUri, transfer.Hash)
                : StellarFiles.ServerFilesUploadFullPath(session.CdnUri, transfer.Hash);
            var elapsed = Stopwatch.StartNew();
            using var response = await _orchestrator.SendRequestStreamAsync(HttpMethod.Post, uri, content, token).ConfigureAwait(false);
            Logger.LogDebug("[{hash}] Upload Status: {status}, request elapsed {seconds:F3}s", transfer.Hash, response.StatusCode, elapsed.Elapsed.TotalSeconds);
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            if (munged) FileDownloadManager.MungeBuffer(bytes.AsSpan());
        }
    }

    // These are whole-process observations, not allocations/CPU attributed to this plugin.
    // Reading counters does not request a collection or alter GC configuration.
    private readonly record struct UploadDiagnostics(long AllocatedBytes, TimeSpan GcPause,
        int Gen0, int Gen1, int Gen2, double? CpuSeconds)
    {
        public static UploadDiagnostics Capture() => new(GC.GetTotalAllocatedBytes(precise: false),
            GC.GetTotalPauseDuration(), GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), ReadCpuSeconds());

        public void LogChanges(ILogger logger)
        {
            var end = Capture();
            var cpu = end.CpuSeconds.HasValue && CpuSeconds.HasValue
                ? Math.Max(0, end.CpuSeconds.Value - CpuSeconds.Value).ToString("F2", CultureInfo.InvariantCulture)
                : "unavailable";
            logger.LogInformation("Upload batch process diagnostics (whole process, all plugins): GC collections gen0 {gen0}, gen1 {gen1}, gen2 {gen2}; GC pause {pause:F1} ms; managed allocations approximately {allocated:F1} MiB; process CPU seconds {cpu}",
                end.Gen0 - Gen0, end.Gen1 - Gen1, end.Gen2 - Gen2,
                Math.Max(0, (end.GcPause - GcPause).TotalMilliseconds),
                Math.Max(0, end.AllocatedBytes - AllocatedBytes) / 1048576d, cpu);
        }

        private static double? ReadCpuSeconds()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.TotalProcessorTime.TotalSeconds;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
            {
                // OS counter availability must not decide whether an upload succeeds.
                return null;
            }
        }
    }

    private sealed class TransferProgress(UploadFileTransfer transfer) : IProgress<UploadProgress>
    {
        public void Report(UploadProgress value) => transfer.Transferred = value.Uploaded;
    }

    private sealed class UploadSession : IDisposable
    {
        private readonly object _lock = new();
        private readonly CancellationTokenSource _cancellation;
        private bool _disposed;
        public Uri CdnUri { get; }
        public long Generation { get; }
        public bool Character { get; }
        public int Limit { get; }
        public bool Alternative { get; }
        public CancellationToken Token { get; }

        public UploadSession(Uri cdnUri, long generation, bool character, int limit, bool alternative, CancellationToken token)
        {
            CdnUri = cdnUri;
            Generation = generation;
            Character = character;
            Limit = limit;
            Alternative = alternative;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            Token = _cancellation.Token;
        }

        public void Cancel()
        {
            lock (_lock) { if (!_disposed) _cancellation.Cancel(); }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _cancellation.Dispose();
            }
        }
    }
}
