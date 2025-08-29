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
using Penumbra.Api.Enums;

namespace StellarSync.Services
{
    public class ModIntegrationService : IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IDalamudPluginInterface _pluginInterface;
        
        // Glamourer API
        private readonly Glamourer.Api.IpcSubscribers.ApiVersion _glamourerApiVersion;
        private readonly GetStateBase64? _glamourerGetAllCustomization;
        private readonly ApplyState? _glamourerApplyAll;
        
        // Penumbra API
        private readonly GetModDirectory _penumbraGetModDirectory;
        private readonly GetGameObjectResourcePaths _penumbraResourcePaths;
        private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;
        private readonly RedrawObject _penumbraRedraw;
        
        public bool GlamourerAvailable { get; private set; }
        public bool PenumbraAvailable { get; private set; }
        
        // Stored data for testing
        private string _storedGlamourerData = "";
        private string _storedPenumbraMetaData = "";
        private Dictionary<string, HashSet<string>> _storedPenumbraData = new();
        
        public ModIntegrationService(IPluginLog logger, IDalamudPluginInterface pluginInterface)
        {
            _logger = logger;
            _pluginInterface = pluginInterface;
            
            // Initialize Glamourer API
            _glamourerApiVersion = new Glamourer.Api.IpcSubscribers.ApiVersion(pluginInterface);
            _glamourerGetAllCustomization = new GetStateBase64(pluginInterface);
            _glamourerApplyAll = new ApplyState(pluginInterface);
            
            // Initialize Penumbra API
            _penumbraGetModDirectory = new GetModDirectory(pluginInterface);
            _penumbraResourcePaths = new GetGameObjectResourcePaths(pluginInterface);
            _penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pluginInterface);
            _penumbraRedraw = new RedrawObject(pluginInterface);
            
            CheckAPIs();
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
                var objectIndex = GetObjectIndexFromAddress(characterAddress);
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
		var objectIndex = GetObjectIndexFromAddress(characterAddress);
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
        
private int GetObjectIndexFromAddress(IntPtr address)
{
	// This is a simplified version - in the real implementation,
	// you'd need to properly convert the address to an object index
	// For now, we'll use a basic approach
	try
	{
		// This is a placeholder - the actual implementation would need
		// to properly handle the address to index conversion
		return 0; // Default to player object
	}
	catch
	{
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
        
        public async Task<string> ApplyGlamourerData(string glamourerData)
        {
            if (!GlamourerAvailable || _glamourerApplyAll == null)
            {
                return "Glamourer API not available";
            }
            
            try
            {
                _logger.Information("Applying stored Glamourer data to character");
                
                // Get object index for player character
                var objectIndex = GetObjectIndexFromAddress(IntPtr.Zero);
                if (objectIndex == -1) return "Invalid object index";
                
                // Apply the Glamourer data using the correct API
                // Based on the old code: _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                const uint LockCode = 0x6D617265; // "mare" in hex
                var result = _glamourerApplyAll.Invoke(glamourerData, (ushort)objectIndex, LockCode);
                
                if (result == Glamourer.Api.Enums.GlamourerApiEc.Success)
                {
                    _logger.Information("Successfully applied Glamourer data to character");
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
        
        public async Task<string> ApplyPenumbraMetaData(string metaData)
        {
            if (!PenumbraAvailable)
            {
                return "Penumbra API not available";
            }
            
            try
            {
                _logger.Information("Applying stored Penumbra meta data to character");
                
                // For Penumbra meta data, we need to trigger a redraw to apply the changes
                // The meta data is already applied to the player, we just need to redraw
                var objectIndex = GetObjectIndexFromAddress(IntPtr.Zero);
                if (objectIndex == -1) return "Invalid object index";
                
                // Trigger a Penumbra redraw to apply meta manipulations
                _penumbraRedraw.Invoke((ushort)objectIndex, RedrawType.Redraw);
                
                _logger.Information("Successfully triggered Penumbra redraw for meta data");
                return "Redraw triggered successfully";
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

// File transfer methods
public async Task<Dictionary<string, byte[]>> GetPenumbraFilesForTransfer(Dictionary<string, HashSet<string>> filePaths)
{
	var files = new Dictionary<string, byte[]>();
	
	try
	{
		if (!PenumbraAvailable)
		{
			_logger.Warning("Penumbra not available for file transfer");
			return files;
		}
		
		// Get Penumbra mod directory
		var modDirectory = _penumbraGetModDirectory.Invoke();
		if (string.IsNullOrEmpty(modDirectory))
		{
			_logger.Warning("Penumbra mod directory not found");
			return files;
		}
		
		_logger.Information($"Reading Penumbra files from: {modDirectory}");
		
		foreach (var kvp in filePaths)
		{
			var category = kvp.Key;
			var paths = kvp.Value;
			
			foreach (var path in paths)
			{
				try
				{
					// Construct full file path
					var fullPath = Path.Combine(modDirectory, path);
					if (File.Exists(fullPath))
					{
						// Read and compress the file
						var fileBytes = await File.ReadAllBytesAsync(fullPath);
						var compressedBytes = await CompressBytesAsync(fileBytes);
						
						// Store with a unique key
						var fileKey = $"{category}_{path.Replace('/', '_').Replace('\\', '_')}";
						files[fileKey] = compressedBytes;
						
						_logger.Information($"Prepared file for transfer: {path} ({fileBytes.Length} -> {compressedBytes.Length} bytes)");
					}
					else
					{
						_logger.Warning($"File not found: {fullPath}");
					}
				}
				catch (Exception ex)
				{
					_logger.Error($"Failed to read file {path}: {ex.Message}");
				}
			}
		}
		
		_logger.Information($"Prepared {files.Count} files for transfer");
		return files;
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to prepare Penumbra files for transfer: {ex.Message}");
		return files;
	}
}

public async Task<string> ApplyPenumbraFilesFromTransfer(Dictionary<string, byte[]> compressedFiles)
{
	try
	{
		if (!PenumbraAvailable)
		{
			return "Penumbra API not available";
		}
		
		// Get Penumbra mod directory
		var modDirectory = _penumbraGetModDirectory.Invoke();
		if (string.IsNullOrEmpty(modDirectory))
		{
			return "Penumbra mod directory not found";
		}
		
		_logger.Information($"Applying {compressedFiles.Count} Penumbra files to: {modDirectory}");
		
		var appliedCount = 0;
		var errorCount = 0;
		
		foreach (var kvp in compressedFiles)
		{
			try
			{
				var fileKey = kvp.Key;
				var compressedBytes = kvp.Value;
				
				// Decompress the file
				var fileBytes = await DecompressBytesAsync(compressedBytes);
				
				// Extract original path from key
				var originalPath = ExtractPathFromKey(fileKey);
				var fullPath = Path.Combine(modDirectory, originalPath);
				
				// Ensure directory exists
				var directory = Path.GetDirectoryName(fullPath);
				if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}
				
				// Write the file
				await File.WriteAllBytesAsync(fullPath, fileBytes);
				appliedCount++;
				
				_logger.Information($"Applied file: {originalPath}");
			}
			catch (Exception ex)
			{
				_logger.Error($"Failed to apply file {kvp.Key}: {ex.Message}");
				errorCount++;
			}
		}
		
		// Trigger Penumbra redraw to apply the new files
		var objectIndex = GetObjectIndexFromAddress(IntPtr.Zero);
		if (objectIndex != -1)
		{
			_penumbraRedraw.Invoke((ushort)objectIndex, RedrawType.Redraw);
			_logger.Information("Triggered Penumbra redraw after file application");
		}
		
		return $"Applied {appliedCount} files successfully, {errorCount} errors";
	}
	catch (Exception ex)
	{
		_logger.Error($"Failed to apply Penumbra files from transfer: {ex.Message}");
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
        
        public void Dispose()
        {
            // Clean up any resources if needed
        }
    }
}
