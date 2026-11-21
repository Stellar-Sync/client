using StellarSync.API.Data;
using StellarSync.Services;
using StellarSync.Services.Mediator;
using StellarSync.Utils;
using StellarSync.WebAPI;
using StellarSync.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace StellarSync.PlayerData.Pairs;

public class VisibleUserDataDistributor : DisposableMediatorSubscriberBase
{
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly PairManager _pairManager;
    private readonly object _stateLock = new();
    private CharacterData? _lastCreatedData;
    private string? _uploadingDataHash;
    private readonly List<UserData> _previouslyVisiblePlayers = [];
    private Task<CharacterData>? _fileUploadTask = null;
    private readonly HashSet<UserData> _usersToPushDataTo = [];
    private readonly SemaphoreSlim _pushDataSemaphore = new(1, 1);
    private readonly CancellationTokenSource _runtimeCts = new();
    private bool _disposed;


    public VisibleUserDataDistributor(ILogger<VisibleUserDataDistributor> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, StellarMediator mediator, FileUploadManager fileTransferManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = msg.CharacterData;
            bool changed;
            lock (_stateLock)
            {
                if (_disposed) return;
                changed = _lastCreatedData == null || !string.Equals(newData.DataHash.Value, _lastCreatedData.DataHash.Value, StringComparison.Ordinal);
                if (changed) _lastCreatedData = newData;
            }
            if (changed)
            {
                Logger.LogTrace("Storing new data hash {hash}", newData.DataHash.Value);
                PushToAllVisibleUsers(forced: true);
            }
            else
            {
                Logger.LogTrace("Data hash {hash} equal to stored data", newData.DataHash.Value);
            }
        });

        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushToAllVisibleUsers());
        Mediator.Subscribe<DisconnectedMessage>(this, (_) =>
        {
            lock (_stateLock)
            {
                _previouslyVisiblePlayers.Clear();
                _usersToPushDataTo.Clear();
                _fileUploadTask = null;
                _uploadingDataHash = null;
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_stateLock)
            {
                if (_disposed) return;
                _disposed = true;
                _fileUploadTask = null;
            }
            _runtimeCts.Cancel();
            _runtimeCts.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PushToAllVisibleUsers(bool forced = false)
    {
        var visibleUsers = _pairManager.GetVisibleUsers();
        int recipientCount;
        string? dataHash;
        lock (_stateLock)
        {
            if (_disposed) return;
            _usersToPushDataTo.UnionWith(visibleUsers);
            recipientCount = _usersToPushDataTo.Count;
            dataHash = _lastCreatedData?.DataHash.Value;
        }

        if (recipientCount > 0)
        {
            Logger.LogDebug("Pushing data {hash} for {count} visible players", dataHash ?? "UNKNOWN", recipientCount);
            PushCharacterData(forced);
        }
    }

    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected) return;

        var allVisibleUsers = _pairManager.GetVisibleUsers();
        List<UserData> newVisibleUsers;
        string? dataHash;
        lock (_stateLock)
        {
            if (_disposed) return;
            newVisibleUsers = allVisibleUsers.Except(_previouslyVisiblePlayers).ToList();
            _previouslyVisiblePlayers.Clear();
            _previouslyVisiblePlayers.AddRange(allVisibleUsers);
            if (newVisibleUsers.Count == 0) return;
            _usersToPushDataTo.UnionWith(newVisibleUsers);
            dataHash = _lastCreatedData?.DataHash.Value;
        }

        Logger.LogDebug("Scheduling character data push of {data} to {users}",
            dataHash ?? string.Empty,
            string.Join(", ", newVisibleUsers.Select(k => k.AliasOrUID)));
        PushCharacterData();
    }

    private void PushCharacterData(bool forced = false)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                Task<CharacterData> uploadTask;
                string uploadHash;
                CancellationToken runtimeToken;
                lock (_stateLock)
                {
                    if (_disposed || _lastCreatedData == null || _usersToPushDataTo.Count == 0) return;
                    runtimeToken = _runtimeCts.Token;
                    runtimeToken.ThrowIfCancellationRequested();
                    uploadHash = _lastCreatedData.DataHash.Value;
                    forced |= !string.Equals(_uploadingDataHash, uploadHash, StringComparison.Ordinal);
                    if (_fileUploadTask == null || _fileUploadTask.IsCompleted || forced)
                    {
                        var uploadingData = _lastCreatedData.DeepClone();
                        Logger.LogDebug("Starting UploadTask for {hash}, Reason: TaskIsNull: {task}, TaskIsCompleted: {taskCpl}, Forced: {frc}",
                            uploadHash, _fileUploadTask == null, _fileUploadTask?.IsCompleted ?? false, forced);
                        _uploadingDataHash = uploadHash;
                        _fileUploadTask = _fileTransferManager.UploadFiles(uploadingData, [.. _usersToPushDataTo]);
                    }
                    uploadTask = _fileUploadTask;
                }

                var dataToSend = await uploadTask.ConfigureAwait(false);
                await _pushDataSemaphore.WaitAsync(runtimeToken).ConfigureAwait(false);
                try
                {
                    List<UserData> recipients;
                    lock (_stateLock)
                    {
                        runtimeToken.ThrowIfCancellationRequested();
                        if (_disposed || !_apiController.IsConnected || !ReferenceEquals(_fileUploadTask, uploadTask)
                            || !string.Equals(_lastCreatedData?.DataHash.Value, uploadHash, StringComparison.Ordinal)
                            || _usersToPushDataTo.Count == 0) return;
                        recipients = [.. _usersToPushDataTo];
                    }
                    Logger.LogDebug("Pushing {data} to {users}", dataToSend.DataHash, string.Join(", ", recipients.Select(k => k.AliasOrUID)));
                    await _apiController.PushCharacterData(dataToSend, recipients).ConfigureAwait(false);
                    lock (_stateLock)
                    {
                        if (!_disposed && _apiController.IsConnected && ReferenceEquals(_fileUploadTask, uploadTask)
                            && string.Equals(_lastCreatedData?.DataHash.Value, uploadHash, StringComparison.Ordinal))
                            _usersToPushDataTo.ExceptWith(recipients);
                    }
                }
                finally
                {
                    _pushDataSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogDebug("Character data upload or push was cancelled");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not upload or push character data; recipients remain queued for the next update");
            }
        });
    }
}
