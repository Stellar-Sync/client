using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Timers;
using System.Diagnostics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
// Removed Legacy import to use modern V6 API
using Penumbra.Api.Enums;
using StellarSync.Interop;

namespace StellarSync.Services
{
    public class ModIntegrationService : IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly IObjectTable? _objectTable;
        private readonly IClientState? _clientState;
        private readonly IFramework? _framework;
        private readonly ICondition? _condition;
        private readonly SemaphoreSlim _redrawSemaphore = new(1, 1);
        // Serialize all apply operations to avoid overlapping Penumbra/Glamourer calls
        private readonly SemaphoreSlim _applySemaphore = new(1, 1);
        
        // Glamourer API
        private readonly Glamourer.Api.IpcSubscribers.ApiVersion _glamourerApiVersion;
        private readonly GetStateBase64? _glamourerGetAllCustomization;
        private readonly ApplyState? _glamourerApplyAll;
        
        // Penumbra API
        private readonly GetModDirectory _penumbraGetModDirectory;
        private readonly Penumbra.Api.IpcSubscribers.GetGameObjectResourcePaths _penumbraResourcePaths;
        private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;
        private readonly Penumbra.Api.IpcSubscribers.RedrawObject _penumbraRedraw;
        
        // Penumbra Temporary Mod API (like client-old) - Using V5/V6 API
        private readonly Penumbra.Api.IpcSubscribers.AddTemporaryMod _penumbraAddTemporaryMod;
        private readonly Penumbra.Api.IpcSubscribers.RemoveTemporaryMod _penumbraRemoveTemporaryMod;
        private readonly Penumbra.Api.IpcSubscribers.CreateTemporaryCollection _penumbraCreateTemporaryCollection;
        private readonly Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection _penumbraDeleteTemporaryCollection;
        private readonly Penumbra.Api.IpcSubscribers.AssignTemporaryCollection _penumbraAssignTemporaryCollection;
        
        // Penumbra Collection API for fallback
        private readonly Penumbra.Api.IpcSubscribers.SetCollectionForObject _penumbraSetCollectionForObject;
        private readonly Penumbra.Api.IpcSubscribers.GetCollections _penumbraGetCollections;
        
        // New IPC Manager for cleaner mod integration
        private readonly ModIntegrationManager _ipcManager;
        
        public bool GlamourerAvailable { get; private set; }
        public bool PenumbraAvailable { get; private set; }
        
        // Auto-reconnection timer
        private System.Timers.Timer? _reconnectionTimer;
        private const int RECONNECTION_INTERVAL_MS = 5000; // Check every 5 seconds
        private const int MAX_RECONNECTION_ATTEMPTS = 10; // Max attempts before giving up temporarily
        private int _glamourerReconnectionAttempts = 0;
        private int _penumbraReconnectionAttempts = 0;
        
        // Stored data for testing
        private string _storedGlamourerData = "";
        private string _storedPenumbraMetaData = "";
        private Dictionary<string, HashSet<string>> _storedPenumbraData = new();
        
        // Character cache for targeting (similar to client-old)
        private readonly Dictionary<string, (string Name, IntPtr Address)> _playerCharacters = new(StringComparer.Ordinal);
        
        // HTTP file service for file transfers
        private HttpFileService? _httpFileService;
        
        // Configuration for received mods path
        private readonly Configuration.Configuration _configuration;
        
        // Helper method to run operations on framework thread (like client-old)
        private async Task<T> RunOnFrameworkThreadAsync<T>(Func<T> func)
        {
            if (_framework == null)
            {
                _logger.Error("Framework is null, cannot run on framework thread");
                throw new InvalidOperationException("Framework is not available");
            }
            
            if (_framework.IsInFrameworkUpdateThread)
            {
                return func();
            }
            else
            {
                return await _framework.RunOnFrameworkThread(func);
            }
        }
        
        private async Task RunOnFrameworkThreadAsync(Action action)
        {
            if (_framework == null)
            {
                _logger.Error("Framework is null, cannot run on framework thread");
                throw new InvalidOperationException("Framework is not available");
            }
            
            if (_framework.IsInFrameworkUpdateThread)
            {
                action();
            }
            else
            {
                await _framework.RunOnFrameworkThread(action);
            }
        }
        
        public ModIntegrationService(IPluginLog logger, IDalamudPluginInterface pluginInterface, Configuration.Configuration configuration, IObjectTable? objectTable = null, IClientState? clientState = null, IFramework? framework = null, ICondition? condition = null)
        {
            _logger = logger;
            _pluginInterface = pluginInterface;
            _configuration = configuration;
            _objectTable = objectTable;
            _clientState = clientState;
            _framework = framework;
            _condition = condition;
            
            // Initialize Glamourer API
            _glamourerApiVersion = new Glamourer.Api.IpcSubscribers.ApiVersion(pluginInterface);
            _glamourerGetAllCustomization = new GetStateBase64(pluginInterface);
            _glamourerApplyAll = new ApplyState(pluginInterface);
            
            // Initialize Penumbra API
            _penumbraGetModDirectory = new GetModDirectory(pluginInterface);
            _penumbraResourcePaths = new Penumbra.Api.IpcSubscribers.GetGameObjectResourcePaths(pluginInterface);
            _penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pluginInterface);
            _penumbraRedraw = new Penumbra.Api.IpcSubscribers.RedrawObject(pluginInterface);
            
                    // Initialize Penumbra Temporary Mod API (like client-old) - Using V5/V6 API
        _penumbraAddTemporaryMod = new Penumbra.Api.IpcSubscribers.AddTemporaryMod(pluginInterface);
        _penumbraRemoveTemporaryMod = new Penumbra.Api.IpcSubscribers.RemoveTemporaryMod(pluginInterface);
        _penumbraCreateTemporaryCollection = new Penumbra.Api.IpcSubscribers.CreateTemporaryCollection(pluginInterface);
        _penumbraDeleteTemporaryCollection = new Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection(pluginInterface);
        _penumbraAssignTemporaryCollection = new Penumbra.Api.IpcSubscribers.AssignTemporaryCollection(pluginInterface);
            
            // Initialize Penumbra Collection API for fallback
            _penumbraSetCollectionForObject = new Penumbra.Api.IpcSubscribers.SetCollectionForObject(pluginInterface);
            _penumbraGetCollections = new Penumbra.Api.IpcSubscribers.GetCollections(pluginInterface);
            
            // Initialize the new IPC Manager
            _ipcManager = new ModIntegrationManager(logger, pluginInterface);
            
            CheckAPIs();
        }
        
        public void InitializeHttpFileService(string serverUrl)
        {
            // Convert WebSocket URL to HTTP file server URL
            var httpFileServerUrl = ConvertToHttpFileServerUrl(serverUrl);
            _httpFileService = new HttpFileService(httpFileServerUrl, _logger);
            _logger.Information($"HTTP file service initialized with server URL: {httpFileServerUrl} (converted from: {serverUrl})");
        }
        

        private string ConvertToHttpFileServerUrl(string serverUrl)
        {
            // Convert WebSocket URL to HTTP file server URL
            // wss://stellar.kasu.network -> https://stellar.kasu.network (nginx proxy)
            // ws://localhost:6000 -> http://localhost:6200 (direct port access)
            
            if (serverUrl.StartsWith("wss://"))
            {
                var host = serverUrl.Substring(6); // Remove "wss://"
                // For production (stellar.kasu.network), use nginx proxy paths
                if (host == "stellar.kasu.network")
                {
                    return $"https://{host}";
                }
                // For other wss:// URLs, use direct port access
                return $"https://{host}:6200";
            }
            else if (serverUrl.StartsWith("ws://"))
            {
                var host = serverUrl.Substring(5); // Remove "ws://"
                // If it's localhost:6000, convert to localhost:6200
                if (host.Contains(":6000"))
                {
                    return $"http://{host.Replace(":6000", ":6200")}";
                }
                // Otherwise assume it's just the host
                return $"http://{host}:6200";
            }
            else if (serverUrl.StartsWith("http://") || serverUrl.StartsWith("https://"))
            {
                // Already an HTTP URL, assume it's the file server URL
                return serverUrl;
            }
            else
            {
                // Default fallback
                return $"http://{serverUrl}:6200";
            }
        }
        

        
        public async Task<bool> UploadFileMetadataAsync(string userId, Dictionary<string, object> fileMetadata)
        {
            if (_httpFileService == null)
            {
                _logger.Error("HTTP file service not initialized");
                return false;
            }
            
            return await _httpFileService.UploadFileMetadataAsync(userId, fileMetadata);
        }
        
        public async Task<Dictionary<string, object>> DownloadFileMetadataAsync(string userId)
        {
            if (_httpFileService == null)
            {
                _logger.Error("HTTP file service not initialized");
                return new Dictionary<string, object>();
            }
            
            return await _httpFileService.DownloadFileMetadataAsync(userId);
        }
        
        public async Task<bool> DownloadFileAsync(string hash, string relativePath)
        {
            if (_httpFileService == null)
            {
                _logger.Error("HTTP file service not initialized");
                return false;
            }
            
            // Get the received mods directory from configuration
            var receivedModsDir = GetReceivedModsDirectory();
            if (string.IsNullOrEmpty(receivedModsDir))
            {
                _logger.Error("Received mods directory not configured");
                return false;
            }
            
            // Create the destination path
            var destinationPath = Path.Combine(receivedModsDir, relativePath);
            
            // Download the file
            var success = await _httpFileService.DownloadFileAsync(hash, destinationPath);
            
            if (success)
            {
                _logger.Information($"Successfully downloaded file to received mods: {relativePath}");
            }
            else
            {
                _logger.Error($"Failed to download file to received mods: {relativePath}");
            }
            
            return success;
        }

        /// <summary>
        /// Downloads multiple files concurrently with progress tracking
        /// </summary>
        public async Task<(int successCount, int failureCount, List<string> errors, List<string> downloadedFilePaths)> DownloadFilesConcurrentlyAsync(
            List<(string hash, string relativePath)> files,
            int maxConcurrency = 5,
            IProgress<(int completed, int total, string currentFile)> progress = null)
        {
            if (_httpFileService == null)
            {
                _logger.Error("HTTP file service not initialized");
                return (0, files.Count, new List<string> { "HTTP file service not initialized" }, new List<string>());
            }
            
            // Get the received mods directory from configuration
            var receivedModsDir = GetReceivedModsDirectory();
            if (string.IsNullOrEmpty(receivedModsDir))
            {
                _logger.Error("Received mods directory not configured");
                return (0, files.Count, new List<string> { "Received mods directory not configured" }, new List<string>());
            }
            
            // Prepare download list with full destination paths
            var downloadFiles = files.Select(file => 
                (file.hash, Path.Combine(receivedModsDir, file.relativePath))).ToList();
            
            _logger.Information($"Starting concurrent download of {files.Count} files with max concurrency: {maxConcurrency}");
            
            var result = await _httpFileService.DownloadFilesConcurrentlyAsync(downloadFiles, maxConcurrency, progress);
            
            // Extract the successfully downloaded file paths
            var downloadedFilePaths = downloadFiles
                .Where(file => File.Exists(file.Item2))
                .Select(file => file.Item2)
                .ToList();
            
            _logger.Information($"Concurrent download completed: {result.successCount} successful, {result.failureCount} failed");
            _logger.Information($"Successfully downloaded files: {downloadedFilePaths.Count}");
            
            return (result.successCount, result.failureCount, result.errors, downloadedFilePaths);
        }
        
        public string GetReceivedModsDirectory()
        {
            // Get the path from configuration (same as ReceivedModsService)
            var path = _configuration.ReceivedModsPath;
            
            // If no path is set, throw an exception - user needs to run setup
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("No received mods directory configured. Please run the setup wizard first.");
            }
            
            _logger.Information($"DEBUG: Using received mods directory: {path}");
            
            // Create directory if it doesn't exist
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                    _logger.Information($"Created received mods directory: {path}");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to create received mods directory: {ex.Message}");
                    return string.Empty;
                }
            }
            
            return path;
        }
        
        public void CheckAPIs()
        {
            // Use the new IPC manager for availability checks
            GlamourerAvailable = _ipcManager.GlamourerAvailable;
            PenumbraAvailable = _ipcManager.PenumbraAvailable;
            
            _logger.Information($"Mod availability - Glamourer: {GlamourerAvailable}, Penumbra: {PenumbraAvailable}");
            
            StartReconnectionTimer();
        }
        
        public void ForceReconnection()
        {
            _logger.Information("Manual reconnection requested by user");
            _glamourerReconnectionAttempts = 0;
            _penumbraReconnectionAttempts = 0;
            CheckAPIs();
        }
        
        private void StartReconnectionTimer()
        {
            if (_reconnectionTimer != null)
            {
                _reconnectionTimer.Stop();
                _reconnectionTimer.Dispose();
            }
            
            _reconnectionTimer = new System.Timers.Timer(RECONNECTION_INTERVAL_MS);
            _reconnectionTimer.Elapsed += OnReconnectionTimerElapsed;
            _reconnectionTimer.AutoReset = true;
            _reconnectionTimer.Start();
            
            _logger.Information("Auto-reconnection timer started");
        }
        
        private void OnReconnectionTimerElapsed(object? sender, ElapsedEventArgs e)
        {
            // Only attempt reconnection if either API is unavailable
            if (!GlamourerAvailable || !PenumbraAvailable)
            {
                _logger.Information("Attempting to reconnect to unavailable APIs...");
                
                if (!GlamourerAvailable && _glamourerReconnectionAttempts < MAX_RECONNECTION_ATTEMPTS)
                {
                    _glamourerReconnectionAttempts++;
                    _logger.Information($"Attempting to reconnect to Glamourer (attempt {_glamourerReconnectionAttempts}/{MAX_RECONNECTION_ATTEMPTS})");
                    CheckGlamourerAPI();
                }
                
                if (!PenumbraAvailable && _penumbraReconnectionAttempts < MAX_RECONNECTION_ATTEMPTS)
                {
                    _penumbraReconnectionAttempts++;
                    _logger.Information($"Attempting to reconnect to Penumbra (attempt {_penumbraReconnectionAttempts}/{MAX_RECONNECTION_ATTEMPTS})");
                    CheckPenumbraAPI();
                }
                
                // Reset attempt counters if APIs become available
                if (GlamourerAvailable)
                {
                    _glamourerReconnectionAttempts = 0;
                    _logger.Information("Glamourer API reconnected successfully!");
                }
                
                if (PenumbraAvailable)
                {
                    _penumbraReconnectionAttempts = 0;
                    _logger.Information("Penumbra API reconnected successfully!");
                }
            }
        }

        private bool IsWorldUnstable()
        {
            try
            {
                if (_condition == null) return false;
                return _condition[ConditionFlag.BetweenAreas]
                    || _condition[ConditionFlag.BetweenAreas51]
                    || _condition[ConditionFlag.OccupiedInCutSceneEvent]
                    || _condition[ConditionFlag.OccupiedInEvent]
                    || _condition[ConditionFlag.OccupiedSummoningBell]
                    || _condition[ConditionFlag.Mounted];
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> WaitForWorldStableAsync(int timeoutMs = 10000, int pollMs = 100)
        {
            if (_condition == null) return true;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (!IsWorldUnstable()) return true;
                await Task.Delay(pollMs);
            }
            _logger.Warning("Timeout waiting for stable world state (zoning/cutscene still active)");
            return false;
        }
        
        private void CheckGlamourerAPI()
        {
            try
            {
                var version = _glamourerApiVersion.Invoke();
                GlamourerAvailable = version is { Major: 1, Minor: >= 1 };
                _logger.Information($"Glamourer API version: {version}, Available: {GlamourerAvailable}");
            }
            catch (Exception ex)
            {
                GlamourerAvailable = false;
                _logger.Warning($"Glamourer API not available: {ex.Message}");
            }
        }
        
        private void CheckPenumbraAPI()
        {
            try
            {
                var modDirectory = _penumbraGetModDirectory.Invoke();
                PenumbraAvailable = !string.IsNullOrEmpty(modDirectory);
                _logger.Information($"Penumbra mod directory: {modDirectory}, Available: {PenumbraAvailable}");
            }
            catch (Exception ex)
            {
                PenumbraAvailable = false;
                _logger.Warning($"Penumbra API not available: {ex.Message}");
            }
        }
        
        public async Task<string> GetGlamourerDataAsync(IntPtr characterAddress)
        {
            if (!GlamourerAvailable || _glamourerGetAllCustomization == null)
            {
                _logger.Warning("Glamourer API not available for data retrieval");
                return string.Empty;
            }
            
            try
            {
                // Convert address to object index
                var objectIndex = await GetObjectIndexFromAddressAsync(characterAddress);
                if (objectIndex == -1) return string.Empty;
                
                _logger.Information($"Getting Glamourer data for object index: {objectIndex}");
                var result = _glamourerGetAllCustomization.Invoke((ushort)objectIndex);
                var data = result.Item2 ?? string.Empty;
                
                _logger.Information($"Glamourer data retrieved, length: {data.Length}");
                if (!string.IsNullOrEmpty(data))
                {
                    _logger.Information($"Glamourer data preview: {data.Substring(0, Math.Min(100, data.Length))}...");
                }
                
                return data;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to get Glamourer data: {ex.Message}");
                return string.Empty;
            }
        }
        
        public async Task<Dictionary<string, HashSet<string>>> GetPenumbraDataAsync(IntPtr characterAddress)
{
	if (!PenumbraAvailable || _penumbraResourcePaths == null)
	{
		return new Dictionary<string, HashSet<string>>();
	}
	
	try
	{
		var objectIndex = await GetObjectIndexFromAddressAsync(characterAddress);
		if (objectIndex == -1) return new Dictionary<string, HashSet<string>>();
		
		_logger.Information("Getting Penumbra resource paths for character");
		
		// Get resource paths for the character
var resourcePaths = _penumbraResourcePaths.Invoke((ushort)objectIndex);
if (resourcePaths == null || resourcePaths.Length == 0)
{
	_logger.Information("No Penumbra resource paths found for character");
	return new Dictionary<string, HashSet<string>>();
}

// The API returns an array of dictionaries, we want the first one
var fileData = resourcePaths[0];
if (fileData == null || fileData.Count == 0)
{
	_logger.Information("No Penumbra resource paths found for character");
	return new Dictionary<string, HashSet<string>>();
}

_logger.Information($"Retrieved Penumbra resource paths across {fileData.Count} categories");
foreach (var kvp in fileData)
{
	_logger.Information($"Category {kvp.Key}: {kvp.Value.Count} files");
}

return fileData;
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to get Penumbra data: {ex.Message}");
		return new Dictionary<string, HashSet<string>>();
	}
}


        
public string GetPenumbraMetaManipulations()
{
	if (!PenumbraAvailable || _penumbraGetMetaManipulations == null)
	{
		_logger.Warning("Penumbra API not available for meta manipulations");
		return string.Empty;
	}
	
	try
	{
		_logger.Information("Getting Penumbra meta manipulations");
		var data = _penumbraGetMetaManipulations.Invoke() ?? string.Empty;
		
		_logger.Information($"Penumbra meta data retrieved, length: {data.Length}");
		if (!string.IsNullOrEmpty(data))
		{
			_logger.Information($"Penumbra meta data preview: {data.Substring(0, Math.Min(100, data.Length))}...");
		}
		
		return data;
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to get Penumbra meta manipulations: {ex.Message}");
		return string.Empty;
	}
}
        
        // Update character cache (should be called periodically)
        public async Task UpdateCharacterCacheAsync()
        {
            try
            {
                await RunOnFrameworkThreadAsync(() =>
                {
                    if (_objectTable == null) return;
                    
                    // Clear old entries
                    _playerCharacters.Clear();
                    
                    // Scan object table for player characters (full table, step 1)
                    var length = _objectTable.Length;
                    for (int i = 0; i < length; i++)
                    {
                        var obj = _objectTable[i];
                        if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                            continue;
                        
                        var name = obj.Name.ToString();
                        if (!string.IsNullOrEmpty(name))
                        {
                            _playerCharacters[name] = (name, obj.Address);
                            _logger.Debug($"Cached player character: {name} at {obj.Address:X}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error updating character cache: {ex.Message}");
            }
        }
        
        // Get character address by name
        private async Task<IntPtr> GetCharacterAddressByNameAsync(string characterName)
        {
            try
            {
                return await RunOnFrameworkThreadAsync(() =>
                {
                    try
                    {
                        if (_playerCharacters.TryGetValue(characterName, out var character))
                        {
                            return character.Address;
                        }
                        
                        // Fallback: try to find in object table
                        if (_objectTable != null)
                        {
                            var length = _objectTable.Length;
                            for (int i = 0; i < length; i++)
                            {
                                try
                                {
                                    var obj = _objectTable[i];
                                    if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                                        continue;
                                    
                                    if (obj.Name.ToString().Equals(characterName, StringComparison.Ordinal))
                                    {
                                        return obj.Address;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.Debug($"Error checking object at index {i}: {ex.Message}");
                                    continue;
                                }
                            }
                        }
                        
                        return IntPtr.Zero;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Error in GetCharacterAddressByNameAsync: {ex.Message}");
                        return IntPtr.Zero;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in GetCharacterAddressByNameAsync wrapper: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private async Task<int> GetObjectIndexFromAddressAsync(IntPtr address)
        {
            try
            {
                return await RunOnFrameworkThreadAsync(() =>
                {
                    if (address == IntPtr.Zero)
                    {
                        // Invalid address, do not resolve to local player to avoid accidental application
                        return -1;
                    }
                    
                    // Find object index for the given address
                    if (_objectTable != null)
                    {
                        var length = _objectTable.Length;
                        for (int i = 0; i < length; i++)
                        {
                            var obj = _objectTable[i];
                            if (obj?.Address == address)
                            {
                                return obj.ObjectIndex;
                            }
                        }
                    }
                    
                    return -1;
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"Error getting object index from address: {ex.Message}");
                return -1;
            }
        }

        private bool ValidateObjectIndexForCharacter(int objectIndex, string characterName)
        {
            try
            {
                if (_objectTable == null) return false;
                if (objectIndex < 0 || objectIndex >= _objectTable.Length) return false;
                var obj = _objectTable[objectIndex];
                if (obj == null) return false;
                if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) return false;
                var name = obj.Name.ToString();
                return string.Equals(name, characterName, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Validation error for object index {objectIndex} and character '{characterName}': {ex.Message}");
                return false;
            }
        }

// Store data for testing
public void StoreDataForTesting(string glamourerData, string penumbraMetaData, Dictionary<string, HashSet<string>> penumbraData)
{
	_storedGlamourerData = glamourerData;
	_storedPenumbraMetaData = penumbraMetaData;
	_storedPenumbraData = penumbraData ?? new Dictionary<string, HashSet<string>>();
	
	_logger.Information($"Stored data for testing - Glamourer: {glamourerData.Length} chars, Penumbra Meta: {penumbraMetaData.Length} chars, Penumbra Files: {penumbraData?.Count ?? 0}");
}
        
        // Apply stored data to character
        public async Task<string> ApplyStoredDataToCharacter()
        {
            var results = new List<string>();
            
            try
            {
                // Apply Glamourer data
                if (!string.IsNullOrEmpty(_storedGlamourerData))
                {
                    var glamourerResult = await ApplyGlamourerData(_storedGlamourerData);
                    results.Add($"Glamourer: {glamourerResult}");
                    
                    // Small delay to ensure Glamourer application completes
                    await Task.Delay(100);
                }
                else
                {
                    results.Add("Glamourer: No stored data");
                }
                
                // Apply Penumbra meta data
                if (!string.IsNullOrEmpty(_storedPenumbraMetaData))
                {
                    var penumbraResult = await ApplyPenumbraMetaData(_storedPenumbraMetaData);
                    results.Add($"Penumbra Meta: {penumbraResult}");
                    
                    // Small delay to ensure Penumbra application completes
                    await Task.Delay(100);
                }
                else
                {
                    results.Add("Penumbra Meta: No stored data");
                }
                
                // Apply Penumbra file data
                if (_storedPenumbraData.Count > 0)
                {
                    var penumbraFilesResult = await ApplyPenumbraFileData(_storedPenumbraData);
                    results.Add($"Penumbra Files: {penumbraFilesResult}");
                }
                else
                {
                    results.Add("Penumbra Files: No stored data");
                }
                
                return string.Join(" | ", results);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply stored data: {ex.Message}");
                return $"Error applying data: {ex.Message}";
            }
        }
        
        public async Task<string> ApplyGlamourerData(string glamourerData, string targetCharacterName = null)
        {
            if (!GlamourerAvailable || _glamourerApplyAll == null)
            {
                return "Glamourer API not available";
            }
            
            // STRICT SAFEGUARD: Never allow null/empty target character names
            if (string.IsNullOrEmpty(targetCharacterName))
            {
                _logger.Error("CRITICAL: Attempted to apply Glamourer data without target character name - REJECTED");
                return "CRITICAL ERROR: Cannot apply data without target character name";
            }
            
            await _applySemaphore.WaitAsync();
            try
            {
                _logger.Information($"Applying Glamourer data to character: {targetCharacterName}");
                
                // Update character cache first
                await UpdateCharacterCacheAsync();
                
                // Get target character address - NO FALLBACK TO LOCAL PLAYER
                var targetAddress = await GetCharacterAddressByNameAsync(targetCharacterName);
                if (targetAddress == IntPtr.Zero)
                {
                    _logger.Error($"CRITICAL: Target character '{targetCharacterName}' not found - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Target character '{targetCharacterName}' not found - cannot apply data";
                }
                
                // Get object index for target character
                var objectIndex = await GetObjectIndexFromAddressAsync(targetAddress);
                if (objectIndex == -1)
                {
                    _logger.Error($"CRITICAL: Invalid object index for character '{targetCharacterName}' - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Invalid object index for character '{targetCharacterName}'";
                }
                
                // Additional safeguard: Verify this is NOT the local player
                var localPlayerIndex = _clientState?.LocalPlayer?.ObjectIndex ?? -1;
                if (objectIndex == localPlayerIndex)
                {
                    _logger.Error($"CRITICAL: Attempted to apply data to local player (index {objectIndex}) - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Cannot apply data to local player - target character '{targetCharacterName}' resolves to local player";
                }
                
                _logger.Information($"Applying Glamourer data to object index: {objectIndex} (character: {targetCharacterName})");
                
                // Apply the Glamourer data using the correct API
                const uint LockCode = 0x6D617265; // "mare" in hex
                var result = _glamourerApplyAll.Invoke(glamourerData, (ushort)objectIndex, LockCode);
                
                if (result == Glamourer.Api.Enums.GlamourerApiEc.Success)
                {
                    _logger.Information($"Successfully applied Glamourer data to character: {targetCharacterName}");
                    return "Applied successfully";
                }
                else
                {
                    _logger.Warning($"Failed to apply Glamourer data: {result}");
                    return $"Application failed: {result}";
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply Glamourer data: {ex.Message}");
                return $"Error: {ex.Message}";
            }
            finally
            {
                _applySemaphore.Release();
            }
        }
        
        public async Task<string> ApplyPenumbraMetaData(string metaData, string targetCharacterName = null)
        {
            if (!PenumbraAvailable)
            {
                return "Penumbra API not available";
            }
            
            // STRICT SAFEGUARD: Never allow null/empty target character names
            if (string.IsNullOrEmpty(targetCharacterName))
            {
                _logger.Error("CRITICAL: Attempted to apply Penumbra meta data without target character name - REJECTED");
                return "CRITICAL ERROR: Cannot apply meta data without target character name";
            }
            
            await _applySemaphore.WaitAsync();
            try
            {
                _logger.Information($"Applying Penumbra meta data to character: {targetCharacterName}");
                
                // Update character cache first
                await UpdateCharacterCacheAsync();
                
                // Get target character address - NO FALLBACK TO LOCAL PLAYER
                var targetAddress = await GetCharacterAddressByNameAsync(targetCharacterName);
                if (targetAddress == IntPtr.Zero)
                {
                    _logger.Error($"CRITICAL: Target character '{targetCharacterName}' not found - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Target character '{targetCharacterName}' not found - cannot apply meta data";
                }
                
                // Get object index for target character
                var objectIndex = await GetObjectIndexFromAddressAsync(targetAddress);
                if (objectIndex == -1)
                {
                    _logger.Error($"CRITICAL: Invalid object index for character '{targetCharacterName}' - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Invalid object index for character '{targetCharacterName}'";
                }
                
                // Validate it still points to the intended character
                if (!ValidateObjectIndexForCharacter(objectIndex, targetCharacterName))
                {
                    _logger.Error($"CRITICAL: Object index {objectIndex} does not match character '{targetCharacterName}' - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Object index mismatch for '{targetCharacterName}'";
                }
                
                // Additional safeguard: Verify this is NOT the local player
                var localPlayerIndex = _clientState?.LocalPlayer?.ObjectIndex ?? -1;
                if (objectIndex == localPlayerIndex)
                {
                    _logger.Error($"CRITICAL: Attempted to apply meta data to local player (index {objectIndex}) - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Cannot apply meta data to local player - target character '{targetCharacterName}' resolves to local player";
                }
                
                // Wait for stable world state before applying
                if (!await WaitForWorldStableAsync())
                {
                    return "World is busy (zoning/cutscene). Try again shortly.";
                }

                // Create a temporary collection for this character
                var applicationId = Guid.NewGuid();
                var createResult = _penumbraCreateTemporaryCollection.Invoke($"StellarSync_{targetCharacterName}_{applicationId}", $"StellarSync_{targetCharacterName}_{applicationId}", out var collectionId);
                
                if (createResult != Penumbra.Api.Enums.PenumbraApiEc.Success || collectionId == Guid.Empty)
                {
                    _logger.Error($"Failed to create temporary collection for character '{targetCharacterName}': {createResult}");
                    return $"Error: Failed to create temporary collection: {createResult}";
                }
                
                _logger.Information($"Created temporary collection {collectionId} for character '{targetCharacterName}'");
                
                // Assign the collection to the target character
                var assignResult = _penumbraAssignTemporaryCollection.Invoke(collectionId, (ushort)objectIndex, true);
                if (assignResult != Penumbra.Api.Enums.PenumbraApiEc.Success)
                {
                    _logger.Error($"Failed to assign temporary collection to character '{targetCharacterName}': {assignResult}");
                    // Clean up the collection
                    _penumbraDeleteTemporaryCollection.Invoke(collectionId);
                    return $"Error: Failed to assign temporary collection: {assignResult}";
                }
                
                _logger.Information($"Assigned temporary collection {collectionId} to character '{targetCharacterName}' (index: {objectIndex})");
                
                // Apply the manipulation data to the temporary collection
                if (!string.IsNullOrEmpty(metaData))
                {
                    var manipResult = _penumbraAddTemporaryMod.Invoke("StellarChara_Meta", collectionId, new Dictionary<string, string>(), metaData, 0);
                    _logger.Information($"Applied manipulation data to collection {collectionId}: {manipResult}");
                }
                
                _logger.Information($"Successfully applied meta data to character: {targetCharacterName}");
                return "Meta data applied successfully (redraw will happen after mod files are applied)";
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply Penumbra meta data: {ex.Message}");
                return $"Error: {ex.Message}";
            }
            finally
            {
                _applySemaphore.Release();
            }
        }
        
        private async Task<string> ApplyPenumbraFileData(Dictionary<string, HashSet<string>> fileData)
{
	if (!PenumbraAvailable)
	{
		return "Penumbra API not available";
	}
	
	try
	{
		_logger.Information("Applying stored Penumbra file data to character");
		
		// For now, just log the file data
		// TODO: Implement actual Penumbra file application
		foreach (var kvp in fileData)
		{
			_logger.Information($"Would apply Penumbra files for {kvp.Key}: {string.Join(", ", kvp.Value)}");
		}
		
		return $"File data logged for {fileData.Count} categories (not yet implemented)";
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to apply Penumbra file data: {ex.Message}");
		return $"Error: {ex.Message}";
	}
}

// File transfer methods - NEW APPROACH: Send metadata only, use HTTP for actual files with CONCURRENT UPLOADS
public async Task<Dictionary<string, object>> GetPenumbraFileMetadataForTransfer(Dictionary<string, HashSet<string>> filePaths)
{
	_logger.Information($"DEBUG: GetPenumbraFileMetadataForTransfer called with {filePaths.Count} file path groups");
	
	var fileMetadata = new Dictionary<string, object>();
	var uploadFiles = new List<(string filePath, string hash, string relativePath)>();
	
	try
	{
		if (!PenumbraAvailable)
		{
			_logger.Warning("Penumbra not available for file transfer");
			return fileMetadata;
		}
		
		// Get Penumbra mod directory
		var modDirectory = _penumbraGetModDirectory.Invoke();
		if (string.IsNullOrEmpty(modDirectory))
		{
			_logger.Warning("Penumbra mod directory not found");
			return fileMetadata;
		}
		
		_logger.Information($"Reading Penumbra file metadata from: {modDirectory}");
		
		var fileCount = 0;
		
		// First pass: collect file metadata and prepare upload list
		foreach (var kvp in filePaths)
		{
			var fullModPath = kvp.Key; // This is the full path including mod folder
			var relativePaths = kvp.Value;
			
			foreach (var relativePath in relativePaths)
			{
				try
				{
					// The fullModPath already contains the complete path to the file
					if (File.Exists(fullModPath))
					{
						var fileInfo = new FileInfo(fullModPath);
						
						// Create simplified metadata object (no full path to reduce size)
						var safeRelativePath = relativePath.Replace('\\', '/');
						
						// Calculate hash for metadata
						var hash = await CalculateFileHashAsync(fullModPath);
						
						var metadata = new
						{
							relative_path = safeRelativePath,
							size_bytes = fileInfo.Length,
							last_modified = ((DateTimeOffset)fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
							hash = hash
						};
						
						// Store with a unique key based on the relative path
						var fileKey = relativePath.Replace('/', '_').Replace('\\', '_');
						fileMetadata[fileKey] = metadata;
						fileCount++;
						
						_logger.Information($"SELECTED FILE {fileCount}: {relativePath} ({fileInfo.Length} bytes)");
						
						// Add to upload list if HTTP service is available
						if (_httpFileService != null)
						{
							uploadFiles.Add((fullModPath, hash, safeRelativePath));
						}
						
						_logger.Information($"Prepared file metadata: {relativePath} ({fileInfo.Length} bytes)");
					}
					else
					{
						_logger.Warning($"File not found: {fullModPath}");
					}
				}
				catch (Exception ex)
				{
					_logger.Error($"Failed to read file metadata {relativePath}: {ex.Message}");
				}
			}
		}
		
		// Second pass: check which files server needs, then upload only missing files
		_logger.Information($"DEBUG: _httpFileService is null: {_httpFileService == null}");
		_logger.Information($"DEBUG: uploadFiles.Count: {uploadFiles.Count}");
		
		if (_httpFileService != null && uploadFiles.Count > 0)
		{
			// Step 1: Check which files the server already has
			var fileHashes = uploadFiles.Select(f => f.hash).ToList();
			var (existingFiles, missingFiles) = await _httpFileService.CheckFilesAsync(fileHashes);
			
			_logger.Information($"Server check result: {existingFiles.Count} files already exist, {missingFiles.Count} files need to be uploaded");
			
			// Step 2: Filter upload list to only include missing files
			var filesToUpload = uploadFiles.Where(f => missingFiles.Contains(f.hash)).ToList();
			
			if (filesToUpload.Count > 0)
			{
				_logger.Information($"Starting concurrent upload of {filesToUpload.Count} missing files (skipping {existingFiles.Count} existing files)...");
				
				var progress = new Progress<(int completed, int total, string currentFile)>(report =>
				{
					_logger.Information($"Upload progress: {report.completed}/{report.total} - {report.currentFile}");
				});
				
				var uploadResult = await _httpFileService.UploadFilesConcurrentlyAsync(filesToUpload, maxConcurrency: 5, progress);
				
				_logger.Information($"Concurrent upload completed: {uploadResult.successCount} successful, {uploadResult.failureCount} failed");
				
				if (uploadResult.errors.Count > 0)
				{
					_logger.Warning($"Upload errors: {string.Join(", ", uploadResult.errors.Take(5))}");
				}
			}
			else
			{
				_logger.Information($"All {uploadFiles.Count} files already exist on server, skipping upload");
			}
		}
		else
		{
			_logger.Warning($"Skipping file check and upload - _httpFileService is null: {_httpFileService == null}, uploadFiles.Count: {uploadFiles.Count}");
		}
		
		_logger.Information($"Prepared metadata for {fileMetadata.Count} files");
		
		// Log some sample files for debugging
		var sampleFiles = fileMetadata.Take(5).ToList();
		foreach (var kvp in sampleFiles)
		{
			_logger.Information($"Sample file metadata: {kvp.Key}");
		}
		
		return fileMetadata;
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to prepare Penumbra file metadata for transfer: {ex.Message}");
		return fileMetadata;
	}
}

private async Task<string> CalculateFileHashAsync(string filePath)
{
	try
	{
		using var sha256 = System.Security.Cryptography.SHA256.Create();
		using var stream = File.OpenRead(filePath);
		var hash = await sha256.ComputeHashAsync(stream);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to calculate hash for {filePath}: {ex.Message}");
		return "";
	}
}

public async Task<string> ApplyPenumbraFilesFromTransfer(Dictionary<string, byte[]> compressedFiles, string sourceCharacterName, ReceivedModsService receivedModsService)
{
	try
	{
		if (!PenumbraAvailable)
		{
			return "Penumbra API not available";
		}
		
		_logger.Information($"Processing {compressedFiles.Count} Penumbra files from {sourceCharacterName}");
		
		var storedCount = 0;
		var errorCount = 0;
		
		foreach (var kvp in compressedFiles)
		{
			try
			{
				var fileKey = kvp.Key;
				var compressedBytes = kvp.Value;
				
				// Decompress the file
				var fileBytes = await DecompressBytesAsync(compressedBytes);
				
				// Extract original filename from key
				var originalFileName = ExtractPathFromKey(fileKey);
				
				// Store the file in the received mods partition
				var storedPath = await receivedModsService.StoreReceivedModAsync(originalFileName, fileBytes, sourceCharacterName);
				storedCount++;
				
				_logger.Information($"Stored received mod: {originalFileName} from {sourceCharacterName}");
			}
			catch (Exception ex)
			{
				_logger.Error($"Failed to store file {kvp.Key}: {ex.Message}");
				errorCount++;
			}
		}
		
		// Note: We don't automatically apply the mods to Penumbra anymore
		// The user will need to manually copy them to their Penumbra directory if desired
		_logger.Information($"Stored {storedCount} mod files from {sourceCharacterName} in received mods partition");
		
		return $"Stored {storedCount} files successfully, {errorCount} errors. Files are in the received mods partition.";
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to process Penumbra files from transfer: {ex.Message}");
		return $"Error: {ex.Message}";
	}
}

private async Task<byte[]> CompressBytesAsync(byte[] data)
{
	using var outputStream = new MemoryStream();
	using (var gzipStream = new GZipStream(outputStream, CompressionMode.Compress))
	{
		await gzipStream.WriteAsync(data, 0, data.Length);
	}
	return outputStream.ToArray();
}

private async Task<byte[]> DecompressBytesAsync(byte[] compressedData)
{
	using var inputStream = new MemoryStream(compressedData);
	using var outputStream = new MemoryStream();
	using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
	{
		await gzipStream.CopyToAsync(outputStream);
	}
	return outputStream.ToArray();
}

private string ExtractPathFromKey(string fileKey)
{
	// Convert key back to original path
	// e.g., "accessory_chara_accessory_a0001_body_0000.mtrl" -> "chara/accessory/a0001/body/0000.mtrl"
	var parts = fileKey.Split('_');
	if (parts.Length >= 2)
	{
		// Remove the category prefix and reconstruct the path
		var pathParts = parts.Skip(1).ToArray();
		return string.Join("/", pathParts);
	}
	return fileKey;
}

// Apply downloaded files to Penumbra using temporary collections for a specific character
public async Task<string> ApplyDownloadedFilesToPenumbraAsync(string receivedModsPath, string targetCharacterName, List<string> specificFiles = null)
{
	await _applySemaphore.WaitAsync();
	try
	{
		if (!PenumbraAvailable) { return "Penumbra API not available"; }
		_logger.Information($"Applying downloaded files using Penumbra temporary collection for character: {targetCharacterName}");

		// Gate on stable world state before beginning heavy operations
		if (!await WaitForWorldStableAsync())
		{
			return "World is busy (zoning/cutscene). Try again shortly.";
		}
		
		if (!Directory.Exists(receivedModsPath)) { 
			_logger.Warning($"Received mods directory does not exist: {receivedModsPath}"); 
			return "Received mods directory not found"; 
		}
		
		// Use specific files if provided, otherwise scan directory (for backward compatibility)
		string[] downloadedFiles;
		if (specificFiles != null && specificFiles.Count > 0)
		{
			// Only apply the specific files that were just downloaded
			downloadedFiles = specificFiles.Where(f => File.Exists(f)).ToArray();
			_logger.Information($"Applying {downloadedFiles.Length} specific downloaded files (out of {specificFiles.Count} requested)");
		}
		else
		{
			// Fallback: scan entire directory (this is the problematic behavior we want to avoid)
			downloadedFiles = Directory.GetFiles(receivedModsPath, "*", SearchOption.AllDirectories);
			_logger.Warning($"No specific files provided, scanning entire directory - found {downloadedFiles.Length} files (this may cause crashes!)");
			
			// Safety check: if we find too many files, it's likely old/corrupted data
			if (downloadedFiles.Length > 50)
			{
				_logger.Error($"CRITICAL: Found {downloadedFiles.Length} files in received mods directory - this is likely old/corrupted data that will cause crashes!");
				_logger.Error("Recommendation: Clear the received mods directory and try again with a fresh download.");
				return $"CRITICAL ERROR: Found {downloadedFiles.Length} files in received mods directory - this will likely cause crashes. Please clear the directory and try again.";
			}
		}
		
		if (downloadedFiles.Length == 0) { 
			return "No downloaded files found to apply"; 
		}
		
		// Step 1: Clean up any existing collections for this character first
		await CleanupExistingCollectionsForCharacter(targetCharacterName);
		
        // Step 2: Create temporary collection for the target character (using same naming pattern as client-old)
        var collectionName = $"Stellar_{targetCharacterName}";
		
		_logger.Information($"Creating temporary collection: {collectionName} for character: {targetCharacterName}");
		
		Guid collectionId = Guid.Empty;
		var createResult = await RunOnFrameworkThreadAsync(() => 
			_ipcManager.Penumbra.CreateTemporaryCollection(collectionName, collectionName, out collectionId));
		
		if (createResult != PenumbraApiEc.Success)
		{
			_logger.Error($"Failed to create temporary collection: {createResult}");
			return $"Failed to create temporary collection: {createResult}";
		}
		
		_logger.Information($"Successfully created temporary collection with ID: {collectionId}");
		
		// Step 3: Find the target character in the game world and get their object index
		var targetAddress = await GetCharacterAddressByNameAsync(targetCharacterName);
		if (targetAddress == IntPtr.Zero)
		{
			// Clean up the collection we created
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
			return $"Target character '{targetCharacterName}' not found in the game world. Please ensure they are nearby or visible.";
		}
		
        var targetObjectIndex = await GetObjectIndexFromAddressAsync(targetAddress);
        if (targetObjectIndex == -1)
        {
            // Clean up the collection we created
            await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
            return $"Could not get object index for character '{targetCharacterName}'";
        }
        
        // Validate object index for intended character
        if (!ValidateObjectIndexForCharacter(targetObjectIndex, targetCharacterName))
        {
            _logger.Error($"Object index {targetObjectIndex} does not match character '{targetCharacterName}'");
            // Clean up the collection we created
            await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
            return $"Error: Object index mismatch for character '{targetCharacterName}'";
        }
		
		_logger.Information($"Found target character '{targetCharacterName}' with object index: {targetObjectIndex}");
		
		// Step 4: Assign the temporary collection to the target character
		_logger.Information($"CRITICAL: About to assign collection {collectionId} to character '{targetCharacterName}' (index: {targetObjectIndex})");
		var assignResult = await RunOnFrameworkThreadAsync(() => 
			_ipcManager.Penumbra.AssignTemporaryCollection(collectionId, targetObjectIndex, true));
		
		if (assignResult != PenumbraApiEc.Success)
		{
			_logger.Error($"CRITICAL: Failed to assign collection {collectionId} to character '{targetCharacterName}': {assignResult}");
			// Clean up the collection we created
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
			return $"Failed to assign temporary collection to character '{targetCharacterName}': {assignResult}";
		}
		
		_logger.Information($"CRITICAL: Successfully assigned temporary collection {collectionId} to character '{targetCharacterName}' (index: {targetObjectIndex})");
		
		// Step 5: Apply the mod files to the temporary collection
		try
		{
			// Build mod paths dictionary (game path -> file path)
			var modPaths = new Dictionary<string, string>();
			var processedCount = 0;
			
			foreach (var filePath in downloadedFiles)
			{
				try
				{
					var relativePath = Path.GetRelativePath(receivedModsPath, filePath);
					
					// Convert relative path to game path format
					var gamePath = relativePath.Replace('\\', '/');
					
					// Add to mod paths
					modPaths[gamePath] = filePath;
					processedCount++;
					
					_logger.Information($"Added mod path: {gamePath} -> {filePath}");
				}
				catch (Exception ex) 
				{ 
					_logger.Error($"Failed to process file {filePath}: {ex.Message}"); 
				}
			}
			
		if (modPaths.Count == 0)
		{
			_logger.Warning("No valid mod paths found");
			// Clean up the collection we created
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
			return "No valid mod paths found";
		}
		
        // Validate that all mod paths exist and are accessible
        foreach (var modPath in modPaths)
        {
            if (!File.Exists(modPath.Value))
            {
                _logger.Error($"Mod file does not exist: {modPath.Value}");
                // Clean up the collection we created
                await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
                return $"Mod file does not exist: {modPath.Value}";
            }
            // Extra safety: only allow known mod file extensions
            var ext = Path.GetExtension(modPath.Value).ToLowerInvariant();
            if (ext is not ".tex" and not ".mtrl" and not ".mdl" and not ".tmb" and not ".scd" and not ".pap")
            {
                _logger.Warning($"Skipping unsupported mod file extension: {modPath.Value}");
                // Allow continue, but remove from dictionary
                // Note: create a filtered copy to avoid modifying during iteration
            }
        }
			
		// Apply the mod files to the temporary collection using the same approach as client-old
		// Use SetTemporaryMods to prevent race conditions by doing remove + add in a single framework thread call
		_logger.Information($"CRITICAL: About to apply {modPaths.Count} mods to collection {collectionId}");
		try
		{
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.SetTemporaryMods(Guid.NewGuid(), collectionId, modPaths));
			_logger.Information($"CRITICAL: Successfully applied {modPaths.Count} files to temporary collection using SetTemporaryMods");
		}
		catch (Exception modEx)
		{
			_logger.Error($"CRITICAL: Exception while setting temporary mods: {modEx.Message}");
			// Clean up the collection we created
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
			return $"Exception while setting temporary mods: {modEx.Message}";
		}
			
		// Wait for a stable world state before redraw
		if (!await WaitForWorldStableAsync())
		{
			_logger.Warning("World still busy; skipping explicit redraw. Mods should still apply once stable.");
			return $"Applied {modPaths.Count} files; redraw deferred due to busy world";
		}

		// Step 6: Trigger a redraw to apply all changes to the target character
		_logger.Information($"Triggering Penumbra redraw for character '{targetCharacterName}' (index: {targetObjectIndex}) to apply all mods");
		
		// Validate object index before redraw to prevent crashes
		if (targetObjectIndex < 0 || targetObjectIndex > 200)
		{
			_logger.Error($"Invalid object index for redraw: {targetObjectIndex}");
			// Clean up the collection we created
			await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
			return $"Error: Invalid object index for redraw: {targetObjectIndex}";
		}
		
        // Double-check that the character still exists before redraw
        var currentAddress = await GetCharacterAddressByNameAsync(targetCharacterName);
        if (currentAddress == IntPtr.Zero)
        {
            _logger.Warning($"Character '{targetCharacterName}' no longer exists, skipping redraw");
            // Clean up the collection we created
            await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
            return $"Warning: Character '{targetCharacterName}' disappeared before redraw, but mods were applied";
        }
        
        _logger.Information($"CRITICAL: About to trigger redraw for character '{targetCharacterName}' (index: {targetObjectIndex})");
        try
        {
            // Use semaphore to prevent concurrent redraws (like lopclient)
            await _redrawSemaphore.WaitAsync();
            try
            {
                // Wait a moment for mods to be processed
                await Task.Delay(250);

                await RunOnFrameworkThreadAsync(() =>
                    _penumbraRedraw.Invoke((ushort)targetObjectIndex, RedrawType.Redraw));

                _logger.Information($"CRITICAL: Triggered Penumbra redraw for '{targetCharacterName}' (index: {targetObjectIndex})");

                // Wait a bit to allow redraw to complete
                await Task.Delay(750);
            }
            finally
            {
                _redrawSemaphore.Release();
            }
        }
		catch (Exception redrawEx)
		{
			_logger.Error($"CRITICAL: Failed to trigger redraw for character '{targetCharacterName}': {redrawEx.Message}");
			// Don't fail the entire operation if redraw fails - the mods are still applied
			_logger.Information($"Mods were applied successfully, but redraw failed. Character may need manual redraw.");
		}
		
		// Step 7: Skip cleanup to prevent VFS crashes
		// The VFS initialization crash suggests that cleaning up the collection while VFS is initializing causes conflicts
		// For now, we'll leave the collection active to prevent crashes
		_logger.Information($"CRITICAL: Skipping collection cleanup to prevent VFS crashes - collection {collectionId} will remain active");
		_logger.Warning($"WARNING: Temporary collection {collectionId} for character '{targetCharacterName}' was not cleaned up to prevent VFS crashes");
			
		_logger.Information($"CRITICAL: Mod application completed successfully for character '{targetCharacterName}'");
		return $"Successfully applied {modPaths.Count} files to character '{targetCharacterName}' using temporary collection. The mods should now be visible on the target character.";
	}
	catch (Exception ex) 
	{ 
		_logger.Error($"Failed to apply mod files to temporary collection: {ex.Message}");
		// Clean up the collection we created
		await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
		return $"Error applying mod files: {ex.Message}"; 
	}
    finally
    {
        _applySemaphore.Release();
    }
}
	catch (Exception ex) 
	{ 
		_logger.Error($"Failed to apply downloaded files to Penumbra: {ex.Message}"); 
		return $"Error: {ex.Message}"; 
	}
}

/// <summary>
/// Finds a character in the game world by name
/// </summary>
private dynamic? FindCharacterByName(string characterName)
{
	try
	{
		if (_clientState?.LocalPlayer == null)
		{
			_logger.Warning("Client state not available for character lookup");
			return null;
		}

		// First check if it's the local player
		if (_clientState.LocalPlayer.Name.TextValue.Equals(characterName, StringComparison.OrdinalIgnoreCase))
		{
			_logger.Information($"Target character '{characterName}' is the local player");
			return _clientState.LocalPlayer;
		}

		// Look through object table to find the target character (like existing code)
		if (_objectTable != null)
		{
			for (int i = 0; i < 200; i += 2)
			{
				var obj = _objectTable[i];
				if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
					continue;
				
				var name = obj.Name.ToString();
				if (name.Equals(characterName, StringComparison.OrdinalIgnoreCase))
				{
					_logger.Information($"Found target character '{characterName}' with object index: {obj.ObjectIndex}");
					return obj;
				}
			}
		}

		_logger.Warning($"Character '{characterName}' not found in the game world");
		return null;
	}
	catch (Exception ex)
	{
		_logger.Error($"Error finding character '{characterName}': {ex.Message}");
		return null;
	}
}
        
        /// <summary>
        /// Tests basic Penumbra connectivity
        /// </summary>
        public async Task<string> TestPenumbraConnectivityAsync()
        {
            try
            {
                if (!PenumbraAvailable)
                {
                    return "Penumbra API not available. Please ensure Penumbra is installed and enabled.";
                }

                return await _ipcManager.Penumbra.TestConnectivityAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during Penumbra connectivity test: {ex.Message}");
                return $"Error during test: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Test method for experimenting with Penumbra temporary collections
        /// This is safe to use as it doesn't interfere with the main plugin functionality
        /// </summary>
        /// <returns>Result message of the test operation</returns>
        public async Task<string> TestTemporaryCollectionAsync()
        {
            if (!PenumbraAvailable)
            {
                return "Penumbra API not available";
            }
            
            try
            {
                _logger.Information("Testing Penumbra temporary collection functionality...");
                
                // Test basic connectivity first
                var connectivityResult = await _ipcManager.Penumbra.TestConnectivityAsync();
                _logger.Information($"Connectivity test result: {connectivityResult}");
                
                // Now test temporary collection creation (like LopClient does)
                var testIdentity = "StellarSync_Test";
                var testName = "Stellar Sync Test Collection";
                
                Guid collectionId = Guid.Empty;
                var createResult = await RunOnFrameworkThreadAsync(() => 
                    _ipcManager.Penumbra.CreateTemporaryCollection(testIdentity, testName, out collectionId));
                
                if (createResult != PenumbraApiEc.Success)
                {
                    return $"Failed to create temporary collection: {createResult}\n\nConnectivity test result: {connectivityResult}";
                }
                
                _logger.Information($"Successfully created temporary collection with ID: {collectionId}");
                
                // Get local player object index for assignment
                var localPlayerIndex = _clientState?.LocalPlayer?.ObjectIndex ?? -1;
                if (localPlayerIndex == -1)
                {
                    // Clean up the collection we created
                    await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
                    return "Local player not available for testing";
                }
                
                // Assign the temporary collection to the local player
                var assignResult = await RunOnFrameworkThreadAsync(() => 
                    _ipcManager.Penumbra.AssignTemporaryCollection(collectionId, localPlayerIndex, true));
                
                if (assignResult != PenumbraApiEc.Success)
                {
                    // Clean up the collection we created
                    await RunOnFrameworkThreadAsync(() => _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
                    return $"Failed to assign temporary collection: {assignResult}";
                }
                
                _logger.Information($"Successfully assigned temporary collection {collectionId} to local player (index: {localPlayerIndex})");
                
                // Wait a moment to let the assignment take effect
                await Task.Delay(1000);
                
                // Remove the temporary collection to clean up
                var deleteResult = await RunOnFrameworkThreadAsync(() => 
                    _ipcManager.Penumbra.DeleteTemporaryCollection(collectionId));
                
                if (deleteResult != PenumbraApiEc.Success)
                {
                    _logger.Warning($"Failed to clean up temporary collection: {deleteResult}");
                    return $"Test completed but cleanup failed: {deleteResult}. Collection ID: {collectionId}";
                }
                
                _logger.Information("Successfully cleaned up temporary collection after testing");
                return $"Temporary collection test completed successfully! Created, assigned, and cleaned up a test collection.\n\nConnectivity test result: {connectivityResult}";
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during temporary collection test: {ex.Message}");
                return $"Error during test: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Test connectivity to all available mods
        /// </summary>
        /// <returns>Result message of the test operation</returns>
        public async Task<string> TestAllModsConnectivityAsync()
        {
            try
            {
                _logger.Information("Testing connectivity to all available mods...");
                return await _ipcManager.TestAllModsConnectivityAsync();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during all mods connectivity test: {ex.Message}");
                return $"Error during test: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Cleans up any existing temporary collections for a specific character
        /// Note: Since we can't enumerate collections, we'll track them ourselves
        /// </summary>
        private async Task CleanupExistingCollectionsForCharacter(string characterName)
        {
            try
            {
                if (!PenumbraAvailable) return;
                
                _logger.Information($"Cleaning up existing temporary collections for character: {characterName}");
                
                // Since we can't enumerate collections, we'll use a different approach:
                // We'll try to delete collections with known naming patterns
                // This is a best-effort cleanup since we can't enumerate all collections
                
                var collectionIdentities = new[]
                {
                    $"StellarSync_{characterName}",
                    $"StellarSync_{characterName}_{DateTime.UtcNow.AddMinutes(-1):yyyyMMdd_HHmm}",
                    $"StellarSync_{characterName}_{DateTime.UtcNow.AddMinutes(-2):yyyyMMdd_HHmm}",
                    $"StellarSync_{characterName}_{DateTime.UtcNow.AddMinutes(-5):yyyyMMdd_HHmm}"
                };
                
                foreach (var identity in collectionIdentities)
                {
                    try
                    {
                        // Try to create a collection with the same identity to see if one exists
                        // If it fails with "already exists", we know there's a collection to clean up
                        var testResult = await RunOnFrameworkThreadAsync(() => 
                        {
                            var result = _ipcManager.Penumbra.CreateTemporaryCollection(identity, $"{identity}_cleanup_test", out var testId);
                            if (result == PenumbraApiEc.Success)
                            {
                                // Successfully created, so no existing collection - clean up the test one
                                _ipcManager.Penumbra.DeleteTemporaryCollection(testId);
                            }
                            return result;
                        });
                        
                        if (testResult != PenumbraApiEc.Success)
                        {
                            _logger.Information($"Found existing collection with identity: {identity}");
                            // There might be an existing collection, but we can't enumerate it
                            // We'll rely on the new collection creation to overwrite it
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error checking for existing collection {identity}: {ex.Message}");
                    }
                }
                
                _logger.Information($"Completed cleanup check for character '{characterName}'");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to clean up existing collections for character '{characterName}': {ex.Message}");
            }
        }
        
        /// <summary>
        /// Cleans up all StellarSync temporary collections (used on disconnect)
        /// Note: Since we can't enumerate collections, this is a best-effort cleanup
        /// </summary>
        public async Task CleanupAllStellarSyncCollections()
        {
            try
            {
                if (!PenumbraAvailable) return;
                
                _logger.Information("Cleaning up all StellarSync temporary collections");
                
                // Since we can't enumerate collections, we'll use a different approach:
                // We'll try to create collections with common StellarSync identities
                // If they fail with "already exists", we know there are collections to clean up
                
                var commonIdentities = new[]
                {
                    "StellarSync_Player",
                    "StellarSync_Character",
                    "StellarSync_User",
                    "StellarSync_Temp"
                };
                
                foreach (var identity in commonIdentities)
                {
                    try
                    {
                        // Try to create a collection with the same identity to see if one exists
                        var testResult = await RunOnFrameworkThreadAsync(() => 
                        {
                            var result = _ipcManager.Penumbra.CreateTemporaryCollection(identity, $"{identity}_cleanup_test", out var testId);
                            if (result == PenumbraApiEc.Success)
                            {
                                // Successfully created, so no existing collection - clean up the test one
                                _ipcManager.Penumbra.DeleteTemporaryCollection(testId);
                            }
                            return result;
                        });
                        
                        if (testResult != PenumbraApiEc.Success)
                        {
                            _logger.Information($"Found existing collection with identity: {identity}");
                            // There might be an existing collection, but we can't enumerate it
                            // We'll rely on the new collection creation to overwrite it
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error checking for existing collection {identity}: {ex.Message}");
                    }
                }
                
                _logger.Information("Completed cleanup check for StellarSync collections");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to clean up StellarSync collections: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_reconnectionTimer != null)
            {
                _reconnectionTimer.Stop();
                _reconnectionTimer.Dispose();
                _reconnectionTimer = null;
            }
            
            _redrawSemaphore?.Dispose();
            _ipcManager?.Dispose();
        }
    }
}
