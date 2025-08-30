using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Dalamud.Plugin.Services;

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
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10); // 10 minute timeout for large files
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

                using var form = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(filePath);
                using var streamContent = new StreamContent(fileStream);
                
                form.Add(streamContent, "file", Path.GetFileName(filePath));
                form.Add(new StringContent(hash), "hash");
                form.Add(new StringContent(relativePath), "relative_path");

                var response = await httpClient.PostAsync($"{serverBaseUrl}/upload", form);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    logger?.Information($"Successfully uploaded file {hash} ({Path.GetFileName(filePath)})");
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
                    using var fileStream = File.Create(destinationPath);
                    await response.Content.CopyToAsync(fileStream);
                    
                    logger?.Information($"Successfully downloaded file {hash} to {destinationPath}");
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
                var requestData = new
                {
                    user_id = userId,
                    file_metadata = fileMetadata
                };

                var jsonContent = JsonSerializer.Serialize(requestData);
                var stringContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"{serverBaseUrl}/metadata/upload", stringContent);
                
                if (response.IsSuccessStatusCode)
                {
                    logger?.Information($"Successfully uploaded file metadata for user {userId}: {fileMetadata.Count} files");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    logger?.Error($"Failed to upload file metadata for user {userId}: {response.StatusCode} - {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger?.Error($"Exception uploading file metadata for user {userId}: {ex.Message}");
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

        public void Dispose()
        {
            httpClient?.Dispose();
        }
    }
}
