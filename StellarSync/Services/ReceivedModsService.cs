using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using StellarSync.Configuration;

namespace StellarSync.Services
{
    public class ReceivedModsService
    {
        private readonly Configuration.Configuration _configuration;
        private readonly IPluginLog _logger;
        private readonly Dictionary<string, DateTime> _fileTimestamps = new Dictionary<string, DateTime>();

        public ReceivedModsService(Configuration.Configuration configuration, IPluginLog logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Gets the received mods directory
        /// </summary>
        public string GetReceivedModsDirectory()
        {
            var path = _configuration.ReceivedModsPath;
            
            // If no path is set, throw an exception - user needs to run setup
            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException("No received mods directory configured. Please run the setup wizard first.");
            }

            // Create directory if it doesn't exist
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                _logger.Information($"Created received mods directory: {path}");
            }

            return path;
        }

        /// <summary>
        /// Stores a received mod file in the partition
        /// </summary>
        public async Task<string> StoreReceivedModAsync(string fileName, byte[] fileData, string sourceCharacter)
        {
            try
            {
                var modsDir = GetReceivedModsDirectory();
                
                // Create a subdirectory for the source character
                var characterDir = Path.Combine(modsDir, SanitizeFileName(sourceCharacter));
                if (!Directory.Exists(characterDir))
                {
                    Directory.CreateDirectory(characterDir);
                }

                // Create a unique filename with timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var uniqueFileName = $"{timestamp}_{SanitizeFileName(fileName)}";
                var fullPath = Path.Combine(characterDir, uniqueFileName);

                // Write the file
                await File.WriteAllBytesAsync(fullPath, fileData);
                
                // Track the file timestamp for cleanup
                _fileTimestamps[fullPath] = DateTime.Now;
                
                _logger.Information($"Stored received mod: {uniqueFileName} from {sourceCharacter}");
                
                // Check if we need to clean up old files
                await CheckAndCleanupOldFilesAsync();
                
                return fullPath;
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to store received mod {fileName}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the total size of the received mods directory in bytes
        /// </summary>
        public long GetReceivedModsSize()
        {
            try
            {
                var modsDir = GetReceivedModsDirectory();
                if (!Directory.Exists(modsDir))
                    return 0;

                return GetDirectorySize(modsDir);
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to calculate received mods size: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Checks if the received mods directory is over the size limit and cleans up if needed
        /// </summary>
        public async Task CheckAndCleanupOldFilesAsync()
        {
            if (!_configuration.AutoDeleteOldMods)
                return;

            var currentSize = GetReceivedModsSize();
            var maxSizeBytes = _configuration.MaxReceivedModsSizeGB * 1024 * 1024 * 1024;

            if (currentSize <= maxSizeBytes)
                return;

            _logger.Information($"Received mods directory size ({currentSize / (1024 * 1024 * 1024)}GB) exceeds limit ({_configuration.MaxReceivedModsSizeGB}GB). Cleaning up old files...");

            await CleanupOldFilesAsync(maxSizeBytes);
        }

        /// <summary>
        /// Cleans up old files until the directory is under the size limit
        /// </summary>
        private async Task CleanupOldFilesAsync(long targetSizeBytes)
        {
            try
            {
                var modsDir = GetReceivedModsDirectory();
                var allFiles = Directory.GetFiles(modsDir, "*", SearchOption.AllDirectories)
                    .Select(f => new FileInfo(f))
                    .OrderBy(f => f.CreationTime)
                    .ToList();

                var currentSize = allFiles.Sum(f => f.Length);
                var filesToDelete = new List<FileInfo>();

                // Find files to delete (oldest first)
                foreach (var file in allFiles)
                {
                    if (currentSize <= targetSizeBytes)
                        break;

                    filesToDelete.Add(file);
                    currentSize -= file.Length;
                }

                // Delete the files
                foreach (var file in filesToDelete)
                {
                    try
                    {
                        File.Delete(file.FullName);
                        _fileTimestamps.Remove(file.FullName);
                        _logger.Information($"Deleted old received mod: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to delete old file {file.Name}: {ex.Message}");
                    }
                }

                // Clean up empty directories
                CleanupEmptyDirectories(modsDir);

                var newSize = GetReceivedModsSize();
                _logger.Information($"Cleanup complete. New size: {newSize / (1024 * 1024)}MB");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to cleanup old files: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes empty directories
        /// </summary>
        private void CleanupEmptyDirectories(string directory)
        {
            try
            {
                var subdirs = Directory.GetDirectories(directory);
                foreach (var subdir in subdirs)
                {
                    CleanupEmptyDirectories(subdir);
                    
                    if (!Directory.EnumerateFileSystemEntries(subdir).Any())
                    {
                        Directory.Delete(subdir);
                        _logger.Information($"Removed empty directory: {subdir}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to cleanup empty directories: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the size of a directory and all its subdirectories
        /// </summary>
        private long GetDirectorySize(string directory)
        {
            var dirInfo = new DirectoryInfo(directory);
            return dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        /// <summary>
        /// Sanitizes a filename to be safe for filesystem
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries))
                .Replace(" ", "_")
                .Replace("\\", "_")
                .Replace("/", "_");
        }

        /// <summary>
        /// Gets information about the received mods partition
        /// </summary>
        public (long currentSizeMB, long maxSizeMB, int fileCount) GetPartitionInfo()
        {
            var modsDir = GetReceivedModsDirectory();
            var currentSize = GetReceivedModsSize();
            var fileCount = Directory.GetFiles(modsDir, "*", SearchOption.AllDirectories).Length;
            
            return (currentSize / (1024 * 1024 * 1024), _configuration.MaxReceivedModsSizeGB, fileCount);
        }

        /// <summary>
        /// Clears all received mods
        /// </summary>
        public void ClearAllReceivedMods()
        {
            try
            {
                var modsDir = GetReceivedModsDirectory();
                if (Directory.Exists(modsDir))
                {
                    Directory.Delete(modsDir, true);
                    Directory.CreateDirectory(modsDir);
                    _fileTimestamps.Clear();
                    _logger.Information("Cleared all received mods");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to clear received mods: {ex.Message}");
            }
        }
    }
}
