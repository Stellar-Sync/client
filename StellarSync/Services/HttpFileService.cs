using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;
using System.Linq;
using Dalamud.Plugin.Services;
using System.IO.Compression;

namespace StellarSync.Services
{
    public class HttpFileService
    {
        private readonly HttpClient httpClient;
        private readonly string serverBaseUrl;
        private readonly IPluginLog logger;

        public HttpFileService(string serverUrl, IPluginLog pluginLog)
        {
            serverBaseUrl = serverUrl;
            logger = pluginLog;
            
            // Debug logging to see what URL this HttpFileService is being created with
            logger?.Information($"DEBUG: HttpFileService constructor called with serverUrl: '{serverUrl}'");
            
            // Configure HttpClient for high concurrency
            var handler = new HttpClientHandler()
            {
                MaxConnectionsPerServer = 20, // Allow many concurrent connections
                UseCookies = false
            };
            httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(10); // 10 minute timeout for large files
            httpClient.DefaultRequestHeaders.ConnectionClose = false;
        }

        public async Task<(List<string> existingFiles, List<string> missingFiles)> CheckFilesAsync(List<string> fileHashes)
        {
            try
            {
                var requestData = new { file_hashes = fileHashes };
                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                logger?.Information($"Checking {fileHashes.Count} files with server...");

                var response = await httpClient.PostAsync($"{serverBaseUrl}/check", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    var existingFiles = new List<string>();
                    var missingFiles = new List<string>();

                    if (result.TryGetProperty("existing_files", out var existingElement))
                    {
                        foreach (var item in existingElement.EnumerateArray())
                        {
                            existingFiles.Add(item.GetString());
                        }
                    }

                    if (result.TryGetProperty("missing_files", out var missingElement))
                    {
                        foreach (var item in missingElement.EnumerateArray())
                        {
                            missingFiles.Add(item.GetString());
                        }
                    }

                    logger?.Information($"File check result: {existingFiles.Count} existing, {missingFiles.Count} missing");
                    return (existingFiles, missingFiles);
                }
                else
                {
                    logger?.Error($"Failed to check files: {response.StatusCode} - {responseContent}");
                    return (new List<string>(), fileHashes); // Assume all files are missing on error
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception checking files: {ex.Message}");
                return (new List<string>(), fileHashes); // Assume all files are missing on error
            }
        }

        public async Task<bool> UploadFileAsync(string filePath, string hash, string relativePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    logger?.Error($"File not found: {filePath}");
                    return false;
                }

                // Read and compress the file using LZ4
                var originalData = await File.ReadAllBytesAsync(filePath);
                var compressedData = await CompressDataAsync(originalData);
                
                logger?.Information($"Compressed {Path.GetFileName(filePath)}: {originalData.Length} -> {compressedData.Length} bytes ({100.0 * compressedData.Length / originalData.Length:F1}%)");

                // Create compressed stream content
                using var compressedStream = new MemoryStream(compressedData);
                using var streamContent = new StreamContent(compressedStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                
                var response = await httpClient.PostAsync($"{serverBaseUrl}/upload/{hash}", streamContent);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    logger?.Information($"Successfully uploaded compressed file {hash} ({Path.GetFileName(filePath)})");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger?.Error($"Failed to upload file {hash}: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception uploading file {hash}: {ex.Message}");
                return false;
            }
        }

        private async Task<byte[]> CompressDataAsync(byte[] data)
        {
            return await Task.Run(() =>
            {
                using var outputStream = new MemoryStream();
                using var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal);
                gzipStream.Write(data, 0, data.Length);
                gzipStream.Flush();
                return outputStream.ToArray();
            });
        }

        public async Task<bool> DownloadFileAsync(string hash, string destinationPath)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var response = await httpClient.GetAsync($"{serverBaseUrl}/download?hash={hash}");
                
                if (response.IsSuccessStatusCode)
                {
                    // Download compressed data
                    var compressedData = await response.Content.ReadAsByteArrayAsync();
                    
                    // Decompress the data before storing (like lopclient does)
                    var decompressedData = await DecompressDataAsync(compressedData);
                    
                    // Write decompressed data to file
                    await File.WriteAllBytesAsync(destinationPath, decompressedData);
                    
                    logger?.Information($"Successfully downloaded and decompressed file {hash} to {destinationPath} ({compressedData.Length} -> {decompressedData.Length} bytes)");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger?.Error($"Failed to download file {hash}: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception downloading file {hash}: {ex.Message}");
                return false;
            }
        }

        private async Task<byte[]> DecompressDataAsync(byte[] compressedData)
        {
            using var inputStream = new MemoryStream(compressedData);
            using var outputStream = new MemoryStream();
            using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                await gzipStream.CopyToAsync(outputStream);
            }
            return outputStream.ToArray();
        }

        public async Task<Dictionary<string, object>> GetFileListAsync()
        {
            try
            {
                var response = await httpClient.GetAsync($"{serverBaseUrl}/list");
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                    logger?.Information($"Retrieved file list with {result?.Count ?? 0} entries");
                    return result ?? new Dictionary<string, object>();
                }
                else
                {
                    logger?.Error($"Failed to get file list: {response.StatusCode}");
                    return new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception getting file list: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        public async Task<bool> UploadFileMetadataAsync(string userId, Dictionary<string, object> fileMetadata)
        {
            try
            {
                logger?.Information($"CRITICAL: Starting metadata upload for user {userId} with {fileMetadata.Count} files");
                logger?.Information($"CRITICAL: Server URL: {serverBaseUrl}");
                
                var requestData = new
                {
                    user_id = userId,
                    file_metadata = fileMetadata
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var stringContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");
                
                logger?.Information($"CRITICAL: Sending POST request to {serverBaseUrl}/metadata/upload");
                var response = await httpClient.PostAsync($"{serverBaseUrl}/metadata/upload", stringContent);
                
                logger?.Information($"CRITICAL: Received response status: {response.StatusCode}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    logger?.Information($"CRITICAL: Successfully uploaded file metadata for user {userId}: {fileMetadata.Count} files");
                    logger?.Information($"CRITICAL: Server response: {responseContent}");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger?.Error($"CRITICAL: Failed to upload file metadata for user {userId}: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"CRITICAL: Exception uploading file metadata for user {userId}: {ex.Message}");
                logger?.Error($"CRITICAL: Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<Dictionary<string, object>> DownloadFileMetadataAsync(string userId)
        {
            try
            {
                var response = await httpClient.GetAsync($"{serverBaseUrl}/metadata/download?user_id={userId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var jsonContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonContent);
                    
                    if (result != null && result.TryGetValue("files", out var filesObj) && filesObj is JsonElement filesElement)
                    {
                        var filesDict = JsonSerializer.Deserialize<Dictionary<string, object>>(filesElement.GetRawText());
                        logger?.Information($"Retrieved file metadata for user {userId}: {filesDict?.Count ?? 0} files");
                        return filesDict ?? new Dictionary<string, object>();
                    }
                    
                    logger?.Warning($"Unexpected response format for file metadata download");
                    return new Dictionary<string, object>();
                }
                else
                {
                    logger?.Error($"Failed to download file metadata for user {userId}: {response.StatusCode}");
                    return new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception downloading file metadata for user {userId}: {ex.Message}");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Uploads multiple files using the chained upload pattern for better concurrency control
        /// </summary>
        public async Task<(int successCount, int failureCount, List<string> errors)> UploadFilesConcurrentlyAsync(
            List<(string filePath, string hash, string relativePath)> files, 
            int maxConcurrency = 5,
            IProgress<(int completed, int total, string currentFile)> progress = null)
        {
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<string>();
            var completed = 0;
            var total = files.Count;

            logger?.Information($"Starting chained upload of {total} files with max concurrency: {maxConcurrency}");

            // Use the chained upload pattern from the old system for better concurrency control
            Task uploadTask = Task.CompletedTask;
            int i = 1;
            
            foreach (var file in files)
            {
                progress?.Report((i, total, Path.GetFileName(file.filePath)));
                logger?.Information($"Starting upload {i}/{total}: {Path.GetFileName(file.filePath)}");
                
                var currentFile = file; // Capture for closure
                var currentIndex = i;
                
                // Wait for the previous upload to complete before starting the next one
                await uploadTask.ConfigureAwait(false);
                
                // Start the next upload
                uploadTask = Task.Run(async () =>
                {
                    try
                    {
                        var startTime = DateTime.Now;
                        logger?.Information($"Starting upload {currentIndex}/{total}: {Path.GetFileName(currentFile.filePath)} at {startTime:HH:mm:ss.fff}");
                        
                        var success = await UploadFileAsync(currentFile.filePath, currentFile.hash, currentFile.relativePath);
                        if (success)
                        {
                            Interlocked.Increment(ref successCount);
                            var endTime = DateTime.Now;
                            var duration = endTime - startTime;
                            logger?.Information($"Completed upload {currentIndex}/{total}: {Path.GetFileName(currentFile.filePath)} at {endTime:HH:mm:ss.fff} (took {duration.TotalSeconds:F2}s)");
                        }
                        else
                        {
                            Interlocked.Increment(ref failureCount);
                            lock (errors)
                            {
                                errors.Add($"Failed to upload: {currentFile.relativePath}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failureCount);
                        lock (errors)
                        {
                            errors.Add($"Exception uploading {currentFile.relativePath}: {ex.Message}");
                        }
                        logger?.Error($"Exception uploading {currentFile.relativePath}: {ex.Message}");
                    }
                });
                
                i++;
            }

            // Wait for the final upload to complete
            await uploadTask.ConfigureAwait(false);
            
            logger?.Information($"Chained upload completed: {successCount} successful, {failureCount} failed");
            return (successCount, failureCount, errors);
        }


        /// <summary>
        /// Downloads multiple files concurrently with configurable concurrency limit
        /// </summary>
        public async Task<(int successCount, int failureCount, List<string> errors)> DownloadFilesConcurrentlyAsync(
            List<(string hash, string destinationPath)> files,
            int maxConcurrency = 5,
            IProgress<(int completed, int total, string currentFile)> progress = null)
        {
            var successCount = 0;
            var failureCount = 0;
            var errors = new List<string>();
            var completed = 0;
            var total = files.Count;

            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = files.Select(async file =>
            {
                await semaphore.WaitAsync();
                try
                {
                    progress?.Report((Interlocked.Increment(ref completed), total, Path.GetFileName(file.destinationPath)));
                    
                    var success = await DownloadFileAsync(file.hash, file.destinationPath);
                    if (success)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failureCount);
                        lock (errors)
                        {
                            errors.Add($"Failed to download: {file.destinationPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failureCount);
                    lock (errors)
                    {
                        errors.Add($"Exception downloading {file.destinationPath}: {ex.Message}");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            return (successCount, failureCount, errors);
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}
