using System.Net;
using System.Text.Json;
using StellarSync.API.Dto.Files;
using StellarSync.WebAPI.Files.Models;

// These stubs supply the plugin environment only. The upload manager, scheduler,
// progress stream, and transfer objects under test are linked production files.
namespace Microsoft.Extensions.Logging
{
    public interface ILogger { }
    public interface ILogger<T> : ILogger { }
    public sealed class TestLogger<T> : ILogger<T> { }
    public static class LoggingExtensions
    {
        public static void LogInformation(this ILogger logger, string text, params object?[] args) { }
        public static void LogDebug(this ILogger logger, string text, params object?[] args) { }
        public static void LogWarning(this ILogger logger, Exception error, string text, params object?[] args) { }
    }
}

namespace StellarSync.API.Data
{
    public sealed class UserData { public string UID { get; set; } = "viewer"; }
    public sealed class CharacterData
    {
        public DataHash DataHash { get; } = new();
        public Dictionary<string, List<FileReplacement>> FileReplacements { get; } = [];
    }
    public sealed class DataHash { public string Value { get; } = "character"; }
    public sealed class FileReplacement
    {
        public string Hash { get; set; } = string.Empty;
        public string FileSwapPath { get; set; } = string.Empty;
    }
}

namespace StellarSync.API.Dto.Files
{
    public interface ITransferFileDto
    {
        string Hash { get; }
        bool IsForbidden { get; }
        string ForbiddenBy { get; }
    }
    public sealed class UploadFileDto : ITransferFileDto
    {
        public string Hash { get; set; } = string.Empty;
        public bool IsForbidden { get; set; }
        public string ForbiddenBy { get; set; } = string.Empty;
    }
    public sealed class DownloadFileDto : ITransferFileDto
    {
        public string Hash { get; set; } = string.Empty;
        public bool IsForbidden { get; set; }
        public string ForbiddenBy { get; set; } = string.Empty;
        public bool FileExists { get; set; }
    }
    public sealed class FilesSendDto
    {
        public List<string> FileHashes { get; set; } = [];
        public List<string> UIDs { get; set; } = [];
    }
}

namespace StellarSync.API.Routes
{
    public static class StellarFiles
    {
        public static Uri ServerFilesDeleteAllFullPath(Uri root) => new(root, "files/delete-all");
        public static Uri ServerFilesFilesSendFullPath(Uri root) => new(root, "files/send");
        public static Uri ServerFilesUploadFullPath(Uri root, string hash) => new(root, $"files/upload/{hash}");
        public static Uri ServerFilesUploadMunged(Uri root, string hash) => new(root, $"files/upload-munged/{hash}");
    }
}

namespace StellarSync.FileCache
{
    public sealed class FileCacheEntity { public string ResolvedFilepath { get; set; } = string.Empty; }
    public sealed class FileCacheManager
    {
        public Dictionary<string, FileCacheEntity> Files { get; } = [];
        public Func<string, CancellationToken, Task<(string, byte[])>> Prepare { get; set; }
            = (hash, _) => Task.FromResult((hash, new byte[] { 1, 2, 3, 4 }));
        public FileCacheEntity? GetFileCacheByHash(string hash) => Files.GetValueOrDefault(hash);
        public Task<(string, byte[])> GetCompressedFileData(string hash, CancellationToken token) => Prepare(hash, token);
    }
}

namespace StellarSync.StellarConfiguration
{
    public sealed class StellarConfigService { public UploadConfig Current { get; } = new(); }
    public sealed class UploadConfig
    {
        public int ParallelUploads { get; set; } = 2;
        public bool UseAlternativeFileUpload { get; set; }
    }
}

namespace StellarSync.Services.Mediator
{
    public sealed record DisconnectedMessage;
    public sealed class StellarMediator
    {
        private readonly Dictionary<Type, List<Delegate>> _subscriptions = [];
        public void Subscribe<T>(object subscriber, Action<T> callback)
        {
            if (!_subscriptions.TryGetValue(typeof(T), out var callbacks))
                _subscriptions[typeof(T)] = callbacks = [];
            callbacks.Add(callback);
        }
        public void Publish<T>(T message)
        {
            if (_subscriptions.TryGetValue(typeof(T), out var callbacks))
                foreach (var callback in callbacks.Cast<Action<T>>()) callback(message);
        }
    }
    public abstract class DisposableMediatorSubscriberBase : IDisposable
    {
        protected Microsoft.Extensions.Logging.ILogger Logger { get; }
        protected StellarMediator Mediator { get; }
        protected DisposableMediatorSubscriberBase(Microsoft.Extensions.Logging.ILogger logger, StellarMediator mediator)
        {
            Logger = logger;
            Mediator = mediator;
        }
        protected virtual void Dispose(bool disposing) { }
        public void Dispose() => Dispose(true);
    }
}

namespace StellarSync.Services.ServerConfiguration
{
    public sealed class ServerConfigurationManager { public string CurrentApiUrl { get; } = "https://sync.test/"; }
}

namespace StellarSync.UI
{
    public static class UiSharedService { public static string ByteToString(long bytes) => bytes.ToString(); }
}

namespace StellarSync.WebAPI.Files
{
    public sealed class FileTransferOrchestrator
    {
        private readonly object _lock = new();
        private readonly List<FileTransfer> _forbidden = [];
        public Uri? FilesCdnUri { get; set; } = new("https://files.test/");
        public bool IsInitialized => FilesCdnUri != null;
        public List<FileTransfer> ForbiddenTransfers { get { lock (_lock) return [.. _forbidden]; } }
        public List<string[]> Negotiations { get; } = [];
        public List<TrackingResponse> Responses { get; } = [];
        public Func<Uri, ProgressableStreamContent, CancellationToken, Task<HttpResponseMessage>> Upload { get; set; }
            = (_, _, _) => Task.FromResult<HttpResponseMessage>(new TrackingResponse(HttpStatusCode.OK));

        public void AddForbiddenTransfer(FileTransfer transfer) { lock (_lock) _forbidden.Add(transfer); }

        public Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri)
            => Task.FromResult<HttpResponseMessage>(new TrackingResponse(HttpStatusCode.OK));

        public Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri, FilesSendDto request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_lock)
            {
                Negotiations.Add([.. request.FileHashes]);
                var response = new TrackingResponse(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(request.FileHashes.Select(hash => new UploadFileDto { Hash = hash }))),
                };
                Responses.Add(response);
                return Task.FromResult<HttpResponseMessage>(response);
            }
        }

        public async Task<HttpResponseMessage> SendRequestStreamAsync(HttpMethod method, Uri uri, ProgressableStreamContent content, CancellationToken token)
        {
            var response = await Upload(uri, content, token);
            if (response is TrackingResponse tracking) { lock (_lock) Responses.Add(tracking); }
            return response;
        }
    }

    public sealed class TrackingResponse(HttpStatusCode status) : HttpResponseMessage(status)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Disposed = true;
            base.Dispose(disposing);
        }
    }

    public static class FileDownloadManager
    {
        public static void MungeBuffer(Span<byte> bytes)
        {
            for (var index = 0; index < bytes.Length; index++) bytes[index] ^= 42;
        }
    }
}
