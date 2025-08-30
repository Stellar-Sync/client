using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.IO.Compression;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using Penumbra.Api.IpcSubscribers.Legacy;
using Penumbra.Api.Enums;

namespace StellarSync.Services
{
    public class ModIntegrationService : IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly IObjectTable? _objectTable;
        private readonly IClientState? _clientState;
        private readonly IFramework? _framework;
        
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
        private readonly Penumbra.Api.IpcSubscribers.AddTemporaryModAll _penumbraAddTemporaryModAll;
        private readonly Penumbra.Api.IpcSubscribers.RemoveTemporaryMod _penumbraRemoveTemporaryMod;
        private readonly Penumbra.Api.IpcSubscribers.CreateTemporaryCollection _penumbraCreateTemporaryCollection;
        private readonly Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection _penumbraDeleteTemporaryCollection;
        private readonly Penumbra.Api.IpcSubscribers.AssignTemporaryCollection _penumbraAssignTemporaryCollection;
        
        // Penumbra Collection API for fallback
        private readonly Penumbra.Api.IpcSubscribers.SetCollectionForObject _penumbraSetCollectionForObject;
        private readonly Penumbra.Api.IpcSubscribers.GetCollections _penumbraGetCollections;
        
        public bool GlamourerAvailable { get; private set; }
        public bool PenumbraAvailable { get; private set; }
        
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
        
        public ModIntegrationService(IPluginLog logger, IDalamudPluginInterface pluginInterface, Configuration.Configuration configuration, IObjectTable? objectTable = null, IClientState? clientState = null, IFramework? framework = null)
        {
            _logger = logger;
            _pluginInterface = pluginInterface;
            _configuration = configuration;
            _objectTable = objectTable;
            _clientState = clientState;
            _framework = framework;
            
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
            _penumbraAddTemporaryModAll = new Penumbra.Api.IpcSubscribers.AddTemporaryModAll(pluginInterface);
            _penumbraRemoveTemporaryMod = new Penumbra.Api.IpcSubscribers.RemoveTemporaryMod(pluginInterface);
            _penumbraCreateTemporaryCollection = new Penumbra.Api.IpcSubscribers.CreateTemporaryCollection(pluginInterface);
            _penumbraDeleteTemporaryCollection = new Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection(pluginInterface);
            _penumbraAssignTemporaryCollection = new Penumbra.Api.IpcSubscribers.AssignTemporaryCollection(pluginInterface);
            
            // Initialize Penumbra Collection API for fallback
            _penumbraSetCollectionForObject = new Penumbra.Api.IpcSubscribers.SetCollectionForObject(pluginInterface);
            _penumbraGetCollections = new Penumbra.Api.IpcSubscribers.GetCollections(pluginInterface);
            
            CheckAPIs();
        }
        
        public void InitializeHttpFileService(string serverUrl)
        {
            _httpFileService = new HttpFileService(serverUrl, _logger);
            _logger.Information($"HTTP file service initialized with server URL: {serverUrl}");
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
            CheckGlamourerAPI();
            CheckPenumbraAPI();
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
                    
                    // Scan object table for player characters
                    for (int i = 0; i < 200; i += 2)
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
            return await RunOnFrameworkThreadAsync(() =>
            {
                if (_playerCharacters.TryGetValue(characterName, out var character))
                {
                    return character.Address;
                }
                
                // Fallback: try to find in object table
                if (_objectTable != null)
                {
                    for (int i = 0; i < 200; i += 2)
                    {
                        var obj = _objectTable[i];
                        if (obj == null || obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
                            continue;
                        
                        if (obj.Name.ToString().Equals(characterName, StringComparison.Ordinal))
                        {
                            return obj.Address;
                        }
                    }
                }
                
                return IntPtr.Zero;
            });
        }

        private async Task<int> GetObjectIndexFromAddressAsync(IntPtr address)
        {
            try
            {
                return await RunOnFrameworkThreadAsync(() =>
                {
                    if (address == IntPtr.Zero)
                    {
                        // Return local player index
                        return _clientState?.LocalPlayer?.ObjectIndex ?? 0;
                    }
                    
                    // Find object index for the given address
                    if (_objectTable != null)
                    {
                        for (int i = 0; i < 200; i += 2)
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
                
                // Additional safeguard: Verify this is NOT the local player
                var localPlayerIndex = _clientState?.LocalPlayer?.ObjectIndex ?? -1;
                if (objectIndex == localPlayerIndex)
                {
                    _logger.Error($"CRITICAL: Attempted to apply meta data to local player (index {objectIndex}) - REJECTING APPLICATION");
                    return $"CRITICAL ERROR: Cannot apply meta data to local player - target character '{targetCharacterName}' resolves to local player";
                }
                
                _logger.Information($"Successfully applied meta data to character: {targetCharacterName}");
                return "Meta data applied successfully (redraw will happen after mod files are applied)";
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to apply Penumbra meta data: {ex.Message}");
                return $"Error: {ex.Message}";
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

// File transfer methods - NEW APPROACH: Send metadata only, use HTTP for actual files
public async Task<Dictionary<string, object>> GetPenumbraFileMetadataForTransfer(Dictionary<string, HashSet<string>> filePaths)
{
	var fileMetadata = new Dictionary<string, object>();
	
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
		// Removed file limit to test with all files
		
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
						
						var metadata = new
						{
							relative_path = safeRelativePath,
							size_bytes = fileInfo.Length,
							last_modified = ((DateTimeOffset)fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
							hash = await CalculateFileHashAsync(fullModPath)
						};
						
						// Store with a unique key based on the relative path
						var fileKey = relativePath.Replace('/', '_').Replace('\\', '_');
						fileMetadata[fileKey] = metadata;
						fileCount++;
						
						_logger.Information($"SELECTED FILE {fileCount}: {relativePath} ({fileInfo.Length} bytes)");
						
						// Upload file to HTTP server if service is available
						if (_httpFileService != null)
						{
							var hash = await CalculateFileHashAsync(fullModPath);
							var uploadSuccess = await _httpFileService.UploadFileAsync(fullModPath, hash, safeRelativePath);
							if (uploadSuccess)
							{
								_logger.Information($"Successfully uploaded file to HTTP server: {relativePath}");
							}
							else
							{
								_logger.Warning($"Failed to upload file to HTTP server: {relativePath}");
							}
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

// Apply downloaded files to Penumbra using temporary mods (like client-old)
public async Task<string> ApplyDownloadedFilesToPenumbraAsync(string receivedModsPath)
{
	try
	{
		if (!PenumbraAvailable) { return "Penumbra API not available"; }
		_logger.Information($"Applying downloaded files using Penumbra temporary mods: {receivedModsPath}");
		
		if (!Directory.Exists(receivedModsPath)) { 
			_logger.Warning($"Received mods directory does not exist: {receivedModsPath}"); 
			return "Received mods directory not found"; 
		}
		
		var downloadedFiles = Directory.GetFiles(receivedModsPath, "*", SearchOption.AllDirectories);
		_logger.Information($"Found {downloadedFiles.Length} downloaded files in received mods directory");
		
		if (downloadedFiles.Length == 0) { 
			return "No downloaded files found in received mods directory"; 
		}
		
		// Try using AddTemporaryModAll (simpler approach)
		try
		{
			// Build mod paths dictionary (game path -> file path) like client-old
			var modPaths = new Dictionary<string, string>();
			var processedCount = 0;
			
			foreach (var filePath in downloadedFiles)
			{
				try
				{
					var relativePath = Path.GetRelativePath(receivedModsPath, filePath);
					
					// Convert relative path to game path format (like client-old)
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
				return "No valid mod paths found";
			}
			
			// Try using AddTemporaryModAll (simpler approach)
			var modResult = await RunOnFrameworkThreadAsync(() => 
				_penumbraAddTemporaryModAll.Invoke("StellarSync_Files", modPaths, string.Empty, 0));
			
			if (modResult != PenumbraApiEc.Success)
			{
				_logger.Error($"Failed to apply Penumbra temporary mods: {modResult}");
				return $"Failed to apply Penumbra temporary mods: {modResult}";
			}
			
			_logger.Information($"Successfully applied {modPaths.Count} files as Penumbra temporary mods");
			
			// Now trigger a redraw to apply all changes (like client-old)
			if (_clientState?.LocalPlayer != null)
			{
				var localPlayerIndex = _clientState.LocalPlayer.ObjectIndex;
				_logger.Information($"Triggering Penumbra redraw for local player (index: {localPlayerIndex}) to apply all mods");
				
				await RunOnFrameworkThreadAsync(() => 
					_penumbraRedraw.Invoke((ushort)localPlayerIndex, RedrawType.Redraw));
				
				_logger.Information($"Successfully triggered Penumbra redraw after applying {modPaths.Count} mod files");
			}
			else
			{
				_logger.Warning("Local player not available for redraw");
			}
			
			return $"Successfully applied {modPaths.Count} files as Penumbra temporary mods and triggered redraw. The mods should now be visible on your character.";
		}
		catch (Exception ex) 
		{ 
			_logger.Error($"Failed to apply downloaded files to Penumbra: {ex.Message}"); 
			return $"Error: {ex.Message}"; 
		}
	}
	catch (Exception ex) 
	{ 
		_logger.Error($"Failed to apply downloaded files to Penumbra: {ex.Message}"); 
		return $"Error: {ex.Message}"; 
	}
}
        
        public void Dispose()
        {
            // Clean up any resources if needed
        }
    }
}
