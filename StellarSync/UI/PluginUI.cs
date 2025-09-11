using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using Dalamud.Interface;
using Newtonsoft.Json;
using StellarSync.Configuration;
using StellarSync.Services;

namespace StellarSync.UI
{
    // Sync progress tracking class
    public class SyncProgress
    {
        public string UserId { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Status { get; set; } = "Waiting...";
        public float Progress { get; set; } = 0.0f; // 0.0 to 1.0
        public DateTime StartTime { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
        
        public string GetElapsedTime()
        {
            var elapsed = DateTime.Now - StartTime;
            if (elapsed.TotalSeconds < 60)
                return $"{elapsed.TotalSeconds:F1}s";
            else if (elapsed.TotalMinutes < 60)
                return $"{elapsed.TotalMinutes:F1}m";
            else
                return $"{elapsed.TotalHours:F1}h";
        }
    }

    public class PluginUI : Window
    {
        private bool isConnected = false;
        private string statusMessage = "Disconnected";
        private Vector4 statusColor = new Vector4(1.0f, 0.0f, 0.0f, 1.0f); // Red
        private bool isSyncing = false;
        private string syncStatus = "Not syncing";
        private string testInfo = "";
        
        // Mod data display
        private string glamourerData = "";
        private string penumbraData = "";
        private string penumbraMetaData = "";
        private bool showModData = false;

private string sendToServerResult = "";
private List<dynamic> onlineUsers = new List<dynamic>();
        private List<dynamic> previousOnlineUsers = new List<dynamic>(); // Track previous user list for disconnect detection
        private string selectedUserId = "";
        private string pairResult = "";
        
        // Loading bar system for sync progress
        private Dictionary<string, SyncProgress> userSyncProgress = new Dictionary<string, SyncProgress>();
        
                // Services
        private NetworkService? networkService;
        private SettingsUI? settingsUI;
        private ModIntegrationService? modIntegrationService;
        private CharacterSyncService? characterSyncService;
        private ReceivedModsService? receivedModsService;
        private IPluginLog? pluginLog;
        private IClientState? clientState;
        private IFramework? framework;

        public PluginUI() : base("Stellar Sync###StellarSyncMainUI")
        {
            // Set window flags for a basic window
            Flags = ImGuiWindowFlags.AlwaysAutoResize;
            
            // Set size constraints
            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(400, 300),
                MaximumSize = new Vector2(800, 600)
            };
            
            // Add title bar buttons like client-old
            TitleBarButtons = new()
            {
                new TitleBarButton()
                {
                    Icon = FontAwesomeIcon.Cog,
                    Click = (msg) =>
                    {
                        if (settingsUI != null)
                            settingsUI.IsOpen = true;
                    },
                    IconOffset = new(2, 1),
                    ShowTooltip = () =>
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text("Open Settings");
                        ImGui.EndTooltip();
                    }
                }
            };
        }

        // Sync progress management methods
        public void StartSyncProgress(string userId, string userName)
        {
            userSyncProgress[userId] = new SyncProgress
            {
                UserId = userId,
                UserName = userName,
                Status = "Starting sync...",
                Progress = 0.0f,
                StartTime = DateTime.Now,
                IsActive = true
            };
        }
        
        public void UpdateSyncProgress(string userId, string status, float progress)
        {
            if (userSyncProgress.ContainsKey(userId))
            {
                userSyncProgress[userId].Status = status;
                userSyncProgress[userId].Progress = Math.Max(0.0f, Math.Min(1.0f, progress));
            }
        }
        
        public void CompleteSyncProgress(string userId)
        {
            if (userSyncProgress.ContainsKey(userId))
            {
                userSyncProgress[userId].Status = "Complete!";
                userSyncProgress[userId].Progress = 1.0f;
                userSyncProgress[userId].IsActive = false;
                
                // Remove after 3 seconds
                Task.Delay(3000).ContinueWith(_ => 
                {
                    if (userSyncProgress.ContainsKey(userId))
                        userSyncProgress.Remove(userId);
                });
            }
        }
        
        public void FailSyncProgress(string userId, string error)
        {
            if (userSyncProgress.ContainsKey(userId))
            {
                userSyncProgress[userId].Status = $"Failed: {error}";
                userSyncProgress[userId].IsActive = false;
                
                // Remove after 5 seconds
                Task.Delay(5000).ContinueWith(_ => 
                {
                    if (userSyncProgress.ContainsKey(userId))
                        userSyncProgress.Remove(userId);
                });
            }
        }
        
        // Draw loading bar for a user
        private void DrawSyncProgressBar(SyncProgress progress)
        {
            var barWidth = 200.0f;
            var barHeight = 8.0f;
            var padding = 4.0f;
            
            // Get cursor position for the bar
            var cursorPos = ImGui.GetCursorPos();
            var barPos = new Vector2(cursorPos.X, cursorPos.Y);
            
            // Draw background bar
            var bgColor = new Vector4(0.2f, 0.2f, 0.2f, 0.8f);
            ImGui.GetWindowDrawList().AddRectFilled(
                barPos,
                barPos + new Vector2(barWidth, barHeight),
                ImGui.ColorConvertFloat4ToU32(bgColor),
                2.0f
            );
            
            // Draw progress bar
            var progressColor = progress.IsActive ? 
                new Vector4(0.2f, 0.8f, 0.2f, 0.9f) : // Green for active
                new Vector4(0.8f, 0.2f, 0.2f, 0.9f);  // Red for failed
                
            ImGui.GetWindowDrawList().AddRectFilled(
                barPos,
                barPos + new Vector2(barWidth * progress.Progress, barHeight),
                ImGui.ColorConvertFloat4ToU32(progressColor),
                2.0f
            );
            
            // Draw border
            var borderColor = new Vector4(0.5f, 0.5f, 0.5f, 0.8f);
            ImGui.GetWindowDrawList().AddRect(
                barPos,
                barPos + new Vector2(barWidth, barHeight),
                ImGui.ColorConvertFloat4ToU32(borderColor),
                2.0f,
                0,
                1.0f
            );
            
            // Move cursor past the bar
            ImGui.SetCursorPos(barPos + new Vector2(0, barHeight + padding));
            
            // Draw status text
            var statusColor = progress.IsActive ? 
                new Vector4(0.8f, 0.8f, 0.8f, 1.0f) : // Light gray for active
                new Vector4(1.0f, 0.5f, 0.5f, 1.0f);  // Light red for failed
                
            ImGui.TextColored(statusColor, progress.Status);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), $"({progress.GetElapsedTime()})");
        }

        public void SetNetworkService(NetworkService networkService)
{
	this.networkService = networkService;
	
	// Wire up message handling
	if (this.networkService != null)
	{
		this.networkService.MessageReceived += OnNetworkMessageReceived;
		this.networkService.Connected += OnNetworkConnected;
		this.networkService.Disconnected += OnNetworkDisconnected;
		this.networkService.ErrorOccurred += OnNetworkError;
	}
}

private void OnNetworkMessageReceived(object? sender, string message)
{
	HandleServerMessage(message);
}

private void OnNetworkConnected(object? sender, EventArgs e)
{
	UpdateConnectionStatus(true, "Connected");
	
	// Automatically upload file metadata when connecting to server
	_ = Task.Run(async () =>
	{
		try
		{
			pluginLog?.Information("CRITICAL: OnNetworkConnected triggered - starting auto-upload process");
			await Task.Delay(2000); // Wait for connection to stabilize
			
			if (modIntegrationService != null)
			{
				var userId = GetPlayerUserId(); // Get current user ID
				pluginLog?.Information($"CRITICAL: Retrieved user ID for auto-upload: {userId}");
				
				if (!string.IsNullOrEmpty(userId))
				{
					pluginLog?.Information("CRITICAL: Starting auto-upload of file metadata after connection...");
					
					// Get Penumbra file metadata and upload it
					var penumbraDataDict = await modIntegrationService.GetPenumbraDataAsync(IntPtr.Zero);
					pluginLog?.Information($"CRITICAL: Retrieved {penumbraDataDict.Count} Penumbra data entries");
					
					if (penumbraDataDict.Count > 0)
					{
						var penumbraFileMetadata = await modIntegrationService.GetPenumbraFileMetadataForTransfer(penumbraDataDict);
						pluginLog?.Information($"CRITICAL: Prepared {penumbraFileMetadata?.Count ?? 0} file metadata entries");
						
						if (penumbraFileMetadata != null && penumbraFileMetadata.Count > 0)
						{
							pluginLog?.Information($"CRITICAL: Attempting to upload {penumbraFileMetadata.Count} file metadata entries for user {userId}");
							var uploadSuccess = await modIntegrationService.UploadFileMetadataAsync(userId, penumbraFileMetadata);
							if (uploadSuccess)
							{
								pluginLog?.Information($"CRITICAL: Successfully auto-uploaded file metadata for {penumbraFileMetadata.Count} files for user {userId}");
							}
							else
							{
								pluginLog?.Error($"CRITICAL: Failed to auto-upload file metadata for user {userId}");
							}
						}
						else
						{
							pluginLog?.Warning($"CRITICAL: No Penumbra file metadata to upload for user {userId}");
						}
					}
					else
					{
						pluginLog?.Information($"CRITICAL: No Penumbra data found for user {userId} - uploading empty metadata to prevent NotFound errors");
						
						// Upload empty metadata to prevent "NotFound" errors when other users try to download
						var emptyMetadata = new Dictionary<string, object>();
						var uploadSuccess = await modIntegrationService.UploadFileMetadataAsync(userId, emptyMetadata);
						if (uploadSuccess)
						{
							pluginLog?.Information($"CRITICAL: Successfully uploaded empty metadata for user {userId} (no mods)");
						}
						else
						{
							pluginLog?.Error($"CRITICAL: Failed to upload empty metadata for user {userId}");
						}
					}
				}
				else
				{
					pluginLog?.Error("CRITICAL: No user ID available for auto-upload");
				}
			}
			else
			{
				pluginLog?.Error("CRITICAL: modIntegrationService is null during auto-upload");
			}
			
			// Also automatically send character data (Glamourer + Penumbra metadata) after metadata upload
			pluginLog?.Information("CRITICAL: Starting automatic character data send after connection...");
			await Task.Delay(1000); // Small delay after metadata upload
			
			try
			{
				// Collect character data - get local player's address instead of IntPtr.Zero
				var localPlayerAddress = await modIntegrationService.GetLocalPlayerAddressAsync();
				var characterData = await modIntegrationService.GetGlamourerDataAsync(localPlayerAddress);
				var penumbraData = await modIntegrationService.GetPenumbraDataAsync(localPlayerAddress); // Back to character-specific
				var metaManipulations = modIntegrationService.GetPenumbraMetaManipulations();
				
				pluginLog?.Information($"CRITICAL: Collected character data - Glamourer: {!string.IsNullOrEmpty(characterData)}, Penumbra files: {penumbraData.Count}, Meta: {!string.IsNullOrEmpty(metaManipulations)}");
				
				// Debug: Log detailed mod information
				if (penumbraData.Count > 0)
				{
					pluginLog?.Information($"CRITICAL: Penumbra data details:");
					foreach (var kvp in penumbraData.Take(5)) // Show first 5 entries
					{
						pluginLog?.Information($"CRITICAL: - {kvp.Key}: {kvp.Value.Count} files");
					}
				}
				else
				{
					pluginLog?.Warning($"CRITICAL: No Penumbra data found - this might indicate mod detection issues");
				}
				
				if (!string.IsNullOrEmpty(metaManipulations))
				{
					pluginLog?.Information($"CRITICAL: Penumbra meta manipulations found: {metaManipulations.Length} characters");
				}
				else
				{
					pluginLog?.Warning($"CRITICAL: No Penumbra meta manipulations found");
				}
				
				// Store the data for display
				this.glamourerData = characterData;
				this.penumbraData = penumbraData.Count > 0 ? $"Files: {penumbraData.Count}" : "No files";
				this.penumbraMetaData = metaManipulations;
				this.showModData = true;
				
				// Send character data to server
				SendCharacterDataToServer();
				pluginLog?.Information("CRITICAL: Successfully sent character data automatically after connection");
			}
			catch (Exception charDataEx)
			{
				pluginLog?.Error($"CRITICAL: Error during automatic character data send: {charDataEx.Message}");
			}
		}
		catch (Exception ex)
		{
			pluginLog?.Error($"CRITICAL: Error during auto-upload of file metadata: {ex.Message}");
			pluginLog?.Error($"CRITICAL: Stack trace: {ex.StackTrace}");
		}
	});
}

private void OnNetworkDisconnected(object? sender, EventArgs e)
{
	UpdateConnectionStatus(false, "Disconnected");
}

private void OnNetworkError(object? sender, string error)
{
	pluginLog?.Error($"Network error: {error}");
	UpdateConnectionStatus(false, $"Error: {error}");
}

        	public void SetSettingsUI(SettingsUI settingsUI)
{
	this.settingsUI = settingsUI;
	
	// Update HttpFileService with the correct server URL if it's already initialized
	if (this.modIntegrationService != null && this.settingsUI != null)
	{
		var serverUrl = this.settingsUI.GetServerUrl();
		
		// Debug logging to see what URL is being used
		pluginLog?.Information($"DEBUG: OnSettingsChanged - GetServerUrl() returned: '{serverUrl}'");
		pluginLog?.Information($"DEBUG: OnSettingsChanged - Final serverUrl being used: '{serverUrl}'");
		
		this.modIntegrationService.InitializeHttpFileService(serverUrl);
	}
}

        	public void SetModIntegrationService(ModIntegrationService modIntegrationService)
{
	this.modIntegrationService = modIntegrationService;
	
	// Initialize HTTP file service for file transfers
	if (this.modIntegrationService != null)
	{
		// Use the same server URL as the network service - it will proxy to file server
		var serverUrl = settingsUI?.GetServerUrl() ?? "wss://stellar.kasu.network";
		
		// Debug logging to see what URL is being used
		pluginLog?.Information($"DEBUG: SetModIntegrationService - settingsUI is null: {settingsUI == null}");
		pluginLog?.Information($"DEBUG: SetModIntegrationService - GetServerUrl() returned: '{settingsUI?.GetServerUrl()}'");
		pluginLog?.Information($"DEBUG: SetModIntegrationService - Final serverUrl being used: '{serverUrl}'");
		
		this.modIntegrationService.InitializeHttpFileService(serverUrl);
		

	}
}

public void SetPluginLog(IPluginLog pluginLog)
{
	this.pluginLog = pluginLog;
}

        public void SetClientState(IClientState clientState)
        {
            this.clientState = clientState;
        }

        public void SetFramework(IFramework framework)
        {
            this.framework = framework;
        }

        public void SetCharacterSyncService(CharacterSyncService characterSyncService)
        {
            this.characterSyncService = characterSyncService;
        }

        public void SetReceivedModsService(ReceivedModsService receivedModsService)
        {
            this.receivedModsService = receivedModsService;
        }

        public void UpdateConnectionStatus(bool connected, string message = "")
        {
            isConnected = connected;
            statusMessage = message;
            statusColor = connected ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(1.0f, 0.0f, 0.0f, 1.0f); // Green : Red
        }

        public void UpdateSyncStatus(bool syncing, string status = "")
        {
            isSyncing = syncing;
            syncStatus = status;
        }

        public void UpdateTestInfo(string info)
        {
            testInfo = info;
        }

        private async void ConnectToServer()
        {
            if (networkService == null) return;
            
            try
            {
                UpdateConnectionStatus(true, "Connecting...");
                
                // Get connection settings from settings UI
                var serverUrl = settingsUI?.GetServerUrl() ?? "wss://stellar.kasu.network";
                var testMode = settingsUI?.GetTestMode() ?? false;
                
                // Debug logging to see what URL is being used
                pluginLog?.Information($"DEBUG: settingsUI is null: {settingsUI == null}");
                pluginLog?.Information($"DEBUG: GetServerUrl() returned: '{settingsUI?.GetServerUrl()}'");
                pluginLog?.Information($"DEBUG: Final serverUrl being used: '{serverUrl}'");
                
                // Update HttpFileService with the correct server URL
                if (modIntegrationService != null)
                {
                    modIntegrationService.InitializeHttpFileService(serverUrl);
                }
                
                if (testMode)
                {
                    // Simulate connection in test mode
                    await Task.Delay(1000); // Simulate connection delay
                    UpdateConnectionStatus(true, "Connected (Test Mode)");
                    UpdateSyncStatus(true, "Syncing active (Test)");
                    UpdateTestInfo("Test mode active - simulating server responses");
                    
                    // Simulate some test data after a delay
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        OnTestPlayerJoined("TestPlayer");
                        
                        await Task.Delay(3000);
                        OnTestCharacterSync();
                        
                        await Task.Delay(2000);
                        OnTestPlayerJoined("AnotherPlayer");
                    });
                }
                else
                {
	// Get the player's character name and current zone
	var characterName = GetPlayerCharacterName();
	var currentZone = GetCurrentZone();
	await networkService.ConnectAsync(serverUrl, characterName, currentZone);
	
	// Request users list after connection
	_ = Task.Run(async () =>
	{
		await Task.Delay(1000); // Wait a bit for connection to stabilize
		await networkService.RequestUsersAsync();
	});
}
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, $"Error: {ex.Message}");
            }
        }

        private async void DisconnectFromServer()
        {
            if (networkService == null) return;
            
            try
            {
                var testMode = settingsUI?.GetTestMode() ?? false;
                
                if (testMode)
                {
                    // Simulate disconnection in test mode
                    UpdateConnectionStatus(false, "Disconnected (Test Mode)");
                    UpdateSyncStatus(false, "Not syncing");
                    UpdateTestInfo("");
                }
                else
                {
                    await networkService.DisconnectAsync();
                    UpdateConnectionStatus(false, "Disconnected");
                    UpdateSyncStatus(false, "Not syncing");
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false, $"Error: {ex.Message}");
            }
        }

        private void OnTestPlayerJoined(string playerName)
        {
            // Simulate player joined event
            UpdateSyncStatus(true, $"Syncing with {playerName} (Test)");
            UpdateTestInfo($"Test: Player '{playerName}' joined");
        }

        private void OnTestCharacterSync()
        {
            // Simulate character sync event
            UpdateSyncStatus(true, "Syncing character data (Test)");
            UpdateTestInfo("Test: Character data synced");
        }

        private async void SaveAndSendCharacterData()
        {
            if (modIntegrationService == null) return;
            
            try
            {
                UpdateTestInfo("Collecting and sending character data...");
                
                // Step 1: Collect character data - get local player's address instead of IntPtr.Zero
                var localPlayerAddress = await modIntegrationService.GetLocalPlayerAddressAsync();
                var characterData = await modIntegrationService.GetGlamourerDataAsync(localPlayerAddress);
                var penumbraData = await modIntegrationService.GetPenumbraDataAsync(localPlayerAddress); // Back to character-specific
                var metaManipulations = modIntegrationService.GetPenumbraMetaManipulations();
                
                // Store the data for display
                this.glamourerData = characterData;
                this.penumbraData = penumbraData.Count > 0 ? $"Files: {penumbraData.Count}" : "No files";
                this.penumbraMetaData = metaManipulations;
                this.showModData = true;
                
                // Store data for testing
                modIntegrationService.StoreDataForTesting(characterData, metaManipulations, penumbraData);
                
                // Step 2: Send to server (if connected)
                if (networkService != null && networkService.IsConnected)
                {
                    SendCharacterDataToServer();
                }
                else
                {
                    sendToServerResult = "Data collected successfully, but not connected to server";
                    UpdateTestInfo("Data collected successfully, but not connected to server");
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error saving and sending character data: {ex.Message}";
                sendToServerResult = errorMessage;
                UpdateTestInfo(errorMessage);
            }
        }

        private async void TestCharacterData()
        {
            if (modIntegrationService == null) return;
            
            try
            {
                UpdateTestInfo("Testing character data collection...");
                
                // Get character data
                var characterData = await modIntegrationService.GetGlamourerDataAsync(IntPtr.Zero);
                var penumbraData = await modIntegrationService.GetPenumbraDataAsync(IntPtr.Zero);
                var metaManipulations = modIntegrationService.GetPenumbraMetaManipulations();
                
                // Store the data for display
                this.glamourerData = characterData;
                this.penumbraData = penumbraData.Count > 0 ? $"Files: {penumbraData.Count}" : "No files";
                this.penumbraMetaData = metaManipulations;
                this.showModData = true;
                
                // Store data for testing
                modIntegrationService.StoreDataForTesting(characterData, metaManipulations, penumbraData);
                
                // Display results
                var glamourerStatus = !string.IsNullOrEmpty(characterData) ? "Data collected" : "No data";
                var penumbraStatus = penumbraData.Count > 0 ? $"{penumbraData.Count} files" : "No files";
                var metaStatus = !string.IsNullOrEmpty(metaManipulations) ? "Available" : "None";
                
                UpdateTestInfo($"Glamourer: {glamourerStatus} | Penumbra: {penumbraStatus} | Meta: {metaStatus}");
            }
            catch (Exception ex)
            {
                UpdateTestInfo($"Error testing character data: {ex.Message}");
            }
        }
        


        private async void SendCharacterDataToServer()
        {
            if (networkService == null || modIntegrationService == null) return;
            
            try
            {
                UpdateTestInfo("Sending character data to server...");
                
                // Get the current character name
                var characterName = GetPlayerCharacterName();
                
                // Get Penumbra file metadata for transfer (upload via HTTP, not WebSocket)
                Dictionary<string, object> penumbraFileMetadata = null;
                try
                {
                    var localPlayerAddress = await modIntegrationService.GetLocalPlayerAddressAsync();
                    var penumbraDataDict = await modIntegrationService.GetPenumbraDataAsync(localPlayerAddress); // Back to character-specific
                    if (penumbraDataDict.Count > 0)
                    {
                        penumbraFileMetadata = await modIntegrationService.GetPenumbraFileMetadataForTransfer(penumbraDataDict);
                        pluginLog?.Information($"DEBUG: Prepared metadata for {penumbraFileMetadata?.Count ?? 0} Penumbra files");
                        
                        // Upload file metadata via HTTP (not WebSocket)
                        if (penumbraFileMetadata != null && penumbraFileMetadata.Count > 0)
                        {
                            var userId = GetPlayerUserId(); // Get current user ID
                            if (!string.IsNullOrEmpty(userId))
                            {
                                var uploadSuccess = await modIntegrationService.UploadFileMetadataAsync(userId, penumbraFileMetadata);
                                if (uploadSuccess)
                                {
                                    pluginLog?.Information($"Successfully uploaded file metadata via HTTP for {penumbraFileMetadata.Count} files");
                                }
                                else
                                {
                                    pluginLog?.Warning($"Failed to upload file metadata via HTTP");
                                }
                            }
                        }
                    }
                }
                catch (Exception fileEx)
                {
                    pluginLog?.Warning($"Failed to prepare Penumbra file metadata for transfer: {fileEx.Message}");
                }
                
                // Get current zone on main thread
                string currentZone = "Unknown";
                if (framework != null)
                {
                    await framework.RunOnFrameworkThread(() =>
                    {
                        currentZone = GetCurrentZone();
                    });
                }
                else
                {
                    currentZone = GetCurrentZone(); // Fallback if framework not available
                }

                // Validate Glamourer data before sending
                string validGlamourerData = glamourerData;
                if (string.IsNullOrEmpty(glamourerData) || glamourerData == "[]" || glamourerData.Trim() == "")
                {
                    pluginLog?.Warning($"CRITICAL: Empty or invalid Glamourer data detected: '{glamourerData}' - will not send to server");
                    validGlamourerData = null; // Don't send empty data
                }
                
                // Create character data object (core data only - file metadata sent via HTTP)
                var characterData = new
                {
                    character_name = characterName,
                    glamourer_data = validGlamourerData, // Use validated data
                    penumbra_meta = penumbraMetaData,
                    // penumbra_file_metadata = penumbraFileMetadata, // File metadata sent via HTTP endpoint
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    zone = currentZone // Add current zone information
                };

                await networkService.SendCharacterDataAsync(characterData);
                
                // Also send name update to ensure the server has the correct name
                if (characterName != "Unknown" && characterName != $"Player_{DateTime.Now:HHmmss}")
                {
                    await networkService.SendNameUpdateAsync(characterName);
                    pluginLog?.Information($"Sent name update: {characterName}");
                }
                
                sendToServerResult = "Character data sent successfully!";
                UpdateTestInfo("Character data sent successfully!");
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error sending data: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" (Inner: {ex.InnerException.Message})";
                }
                sendToServerResult = errorMessage;
                UpdateTestInfo(errorMessage);
            }
        }

        private async void RequestUserData(string userId)
        {
            if (networkService == null)
            {
                pairResult = "Error: Network service not available";
                pluginLog?.Error("Network service is null");
                return;
            }
            
            if (!networkService.IsConnected)
            {
                pairResult = "Error: Not connected to server";
                pluginLog?.Error("Not connected to server");
                return;
            }
            
            try
            {
                // Get user name for progress tracking
                var userName = "Unknown";
                foreach (var user in onlineUsers)
                {
                    if (user?.id?.ToString() == userId)
                    {
                        userName = user?.name?.ToString() ?? "Unknown";
                        break;
                    }
                }
                
                pluginLog?.Information($"Requesting data from user {userId} ({userName})");
                
                // Start sync progress
                StartSyncProgress(userId, userName);
                UpdateSyncProgress(userId, "Requesting data...", 0.1f);
                
                await networkService.RequestUserDataAsync(userId);
                pairResult = $"Requesting data from user {userId} ({userName})...";
                pluginLog?.Information($"Successfully sent request for user {userId} ({userName})");
            }
            catch (Exception ex)
            {
                pairResult = $"Error: {ex.Message}";
                pluginLog?.Error($"Failed to request user data for {userId}: {ex.Message}");
                FailSyncProgress(userId, ex.Message);
            }
        }

        private void HandleServerMessage(string message)
        {
            try
            {
                pluginLog?.Debug($"Received server message of {message.Length} characters");
                
                // Log first 200 characters for debugging
                var preview = message.Length > 200 ? message.Substring(0, 200) + "..." : message;
                pluginLog?.Debug($"Message preview: {preview}");
                
                var messageObj = JsonConvert.DeserializeObject<dynamic>(message);
                var messageType = messageObj?.type?.ToString();
                
                pluginLog?.Debug($"Processing message type: {messageType}");
                
                switch (messageType)
                {
                    case "users_list":
                        HandleUsersList(messageObj?.data);
                        break;
                    case "user_character_data":
                        pluginLog?.Information("Received user character data response");
                        HandleUserCharacterData(messageObj?.data);
                        break;
                    case "sync_request_queued":
                        pluginLog?.Information("Received sync request queued response");
                        HandleSyncRequestQueued(messageObj?.data);
                        break;
                    case "error":
                        pluginLog?.Warning($"Received server error: {messageObj?.error}");
                        HandleServerError(messageObj?.error);
                        break;
                    case "character_data_received":
                        sendToServerResult = "Character data received by server!";
                        break;
                    case "connected":
                        pluginLog?.Information("Connected to server successfully");
                        break;
                    default:
                        pluginLog?.Debug($"Unhandled message type: {messageType}");
                        break;
                }
            }
            catch (JsonException jsonEx)
            {
                pluginLog?.Error($"JSON parsing error: {jsonEx.Message}");
                pluginLog?.Error($"Message length: {message.Length}");
                pluginLog?.Error($"Message start: {message.Substring(0, Math.Min(100, message.Length))}");
                pluginLog?.Error($"Message end: {message.Substring(Math.Max(0, message.Length - 100))}");
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Failed to handle server message: {ex.Message}");
                pluginLog?.Error($"Message length: {message.Length}");
            }
        }

private void HandleUsersList(dynamic usersData)
        {
            try
            {
                // Store current users as previous for comparison
                previousOnlineUsers.Clear();
                foreach (var user in onlineUsers)
                {
                    previousOnlineUsers.Add(user);
                }
                
                // Clear and repopulate current users
                onlineUsers.Clear();
                if (usersData != null)
                {
                    foreach (var user in usersData)
                    {
                        onlineUsers.Add(user);
                    }
                }
                
                // Detect disconnected users and clean up their collections
                _ = Task.Run(async () => await HandleDisconnectedUsers());
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Failed to handle users list: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handles cleanup for users who have disconnected or are no longer visible
        /// </summary>
        private async Task HandleDisconnectedUsers()
        {
            try
            {
                if (modIntegrationService == null) return;
                
                // Get current visible user names
                var currentVisibleNames = GetCurrentlyVisiblePlayerNames();
                
                // Find users who were previously online but are now disconnected or not visible
                var disconnectedUsers = new List<string>();
                
                foreach (var previousUser in previousOnlineUsers)
                {
                    try
                    {
                        var userName = previousUser?.name?.ToString();
                        if (string.IsNullOrEmpty(userName)) continue;
                        
                        // Check if user is still in the current online list
                        var stillOnline = onlineUsers.Any(u => u?.name?.ToString() == userName);
                        
                        // Check if user is still visually visible
                        var stillVisible = currentVisibleNames.Contains(userName);
                        
                        // If user is no longer online OR no longer visible, they should be cleaned up
                        if (!stillOnline || !stillVisible)
                        {
                            disconnectedUsers.Add(userName);
                        }
                    }
                    catch (Exception ex)
                    {
                        pluginLog?.Warning($"Error checking user disconnect status: {ex.Message}");
                    }
                }
                
                // Clean up collections and sync progress for disconnected users
                if (disconnectedUsers.Count > 0)
                {
                    pluginLog?.Information($"Cleaning up collections for {disconnectedUsers.Count} disconnected users: {string.Join(", ", disconnectedUsers)}");
                    
                    foreach (var userName in disconnectedUsers)
                    {
                        try
                        {
                            // Clean up Penumbra collections
                            await modIntegrationService.CleanupExistingCollectionsForCharacter(userName);
                            
                            // Clean up sync progress
                            var userIdToRemove = previousOnlineUsers
                                .FirstOrDefault(u => u?.name?.ToString() == userName)?.id?.ToString();
                            if (!string.IsNullOrEmpty(userIdToRemove) && userSyncProgress.ContainsKey(userIdToRemove))
                            {
                                userSyncProgress.Remove(userIdToRemove);
                            }
                            
                            pluginLog?.Information($"Cleaned up collections and sync progress for disconnected user: {userName}");
                        }
                        catch (Exception ex)
                        {
                            pluginLog?.Warning($"Failed to clean up collections for user {userName}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Error handling disconnected users: {ex.Message}");
            }
        }

private void HandleUserCharacterData(dynamic data)
{
	try
	{
		if (data != null && modIntegrationService != null)
		{
			// Store the received character data
			var characterData = data.data;
			
			// Get the source character name
			var sourceCharacterName = data.source_character_name?.ToString();
			
			// CRITICAL SAFEGUARD: Never apply data without a valid character name
			if (string.IsNullOrEmpty(sourceCharacterName))
			{
				pluginLog?.Error("CRITICAL: Received character data without source character name - REJECTING ALL APPLICATIONS");
				pairResult = "CRITICAL ERROR: Cannot apply data without source character name";
				return;
			}
			
			pluginLog?.Information($"Applying character data to source character: {sourceCharacterName}");
			
			// Get user ID for progress tracking
			var userId = GetUserIdFromCharacterName(sourceCharacterName);
			if (!string.IsNullOrEmpty(userId))
			{
				UpdateSyncProgress(userId, "Applying Glamourer data...", 0.3f);
			}
			
            // Apply the received data to the source character in a single, sequenced task
            _ = Task.Run(async () =>
            {
                try
                {
                    // 1) Glamourer
                    if (characterData?.glamourer_data != null)
                    {
                        await modIntegrationService.ApplyGlamourerData(characterData.glamourer_data.ToString(), sourceCharacterName);
                        if (!string.IsNullOrEmpty(userId))
                        {
                            UpdateSyncProgress(userId, "Applying Penumbra meta...", 0.5f);
                        }
                    }
                    else
                    {
                        // No Glamourer data, skip to next step
                        pluginLog?.Information($"No Glamourer data found for {sourceCharacterName}, skipping Glamourer application");
                        if (!string.IsNullOrEmpty(userId))
                        {
                            UpdateSyncProgress(userId, "Applying Penumbra meta...", 0.5f);
                        }
                    }

                    // 2) Penumbra meta
                    if (characterData?.penumbra_meta != null)
                    {
                        await modIntegrationService.ApplyPenumbraMetaData(characterData.penumbra_meta.ToString(), sourceCharacterName);
                        if (!string.IsNullOrEmpty(userId))
                        {
                            UpdateSyncProgress(userId, "Downloading mod files...", 0.7f);
                        }
                    }
                    else
                    {
                        // No Penumbra metadata, skip to next step
                        pluginLog?.Information($"No Penumbra metadata found for {sourceCharacterName}, skipping metadata application");
                        if (!string.IsNullOrEmpty(userId))
                        {
                            UpdateSyncProgress(userId, "Downloading mod files...", 0.7f);
                        }
                    }

                    // 3) Penumbra file metadata (fetch via HTTP), download and apply
                    pluginLog?.Information($"Fetching file metadata for {sourceCharacterName} via HTTP...");
                    var sourceUserId = GetSourceUserId(data);
                    if (!string.IsNullOrEmpty(sourceUserId))
                    {
                        var fileMetadata = await modIntegrationService.DownloadFileMetadataAsync(sourceUserId);
                        if (fileMetadata != null && fileMetadata.Count > 0)
                        {
                            pluginLog?.Information($"Retrieved file metadata for {fileMetadata.Count} files from {sourceCharacterName}");
                            await DownloadAndApplyFilesAsync(fileMetadata, sourceCharacterName);
                        }
                        else
                        {
                            pluginLog?.Information($"No file metadata found for {sourceCharacterName}");
                        }
                    }
                    else
                    {
                        pluginLog?.Warning($"Could not determine source user ID for {sourceCharacterName}");
                    }
                }
                catch (Exception ex)
                {
                    pluginLog?.Error($"Failed during sequenced character apply: {ex.Message}");
                    if (!string.IsNullOrEmpty(userId))
                    {
                        FailSyncProgress(userId, $"Apply error: {ex.Message}");
                    }
                }
            });
			
			pairResult = "Character data applied successfully!";
		}
	}
	catch (Exception ex)
	{
		pairResult = $"Error applying character data: {ex.Message}";
		pluginLog?.Error($"Failed to handle user character data: {ex.Message}");
	}
}

/// <summary>
/// Handles error messages from the server
/// </summary>
private void HandleServerError(string errorMessage)
{
	try
	{
		if (!string.IsNullOrEmpty(errorMessage))
		{
			pluginLog?.Warning($"Server error: {errorMessage}");
			
			// Update the pair result to show the error
			if (errorMessage.Contains("User data not found"))
			{
				pairResult = "Error: User has not sent any character data yet";
			}
			else if (errorMessage.Contains("User is not online"))
			{
				pairResult = "Error: User is not currently online";
			}
			else
			{
				pairResult = $"Server error: {errorMessage}";
			}
			
			// Clear any sync progress since this is an error
			// Note: We don't know which user this error is for, so we can't update specific progress
		}
	}
	catch (Exception ex)
	{
		pluginLog?.Error($"Error handling server error message: {ex.Message}");
		pairResult = $"Error handling server error: {ex.Message}";
	}
}

/// <summary>
/// Handles sync request queued messages from the server
/// </summary>
private void HandleSyncRequestQueued(dynamic data)
{
	try
	{
		if (data != null)
		{
			var message = data.message?.ToString() ?? "Sync request queued";
			var targetUserId = data.target_user_id?.ToString() ?? "Unknown";
			var status = data.status?.ToString() ?? "queued";
			
			pluginLog?.Information($"Sync request queued: {message}");
			
			// Find the target user name for better display
			var targetUserName = "Unknown User";
			foreach (var user in onlineUsers)
			{
				if (user?.id?.ToString() == targetUserId)
				{
					targetUserName = user?.name?.ToString() ?? "Unknown User";
					break;
				}
			}
			
			// Update the pair result to show the request is queued
			pairResult = $"Sync request queued with {targetUserName}. Waiting for character data...";
			
			// Update sync progress if we have a user ID
			var userId = GetUserIdFromCharacterName(targetUserName);
			if (!string.IsNullOrEmpty(userId))
			{
				UpdateSyncProgress(userId, "Request queued, waiting for data...", 0.1f);
			}
			
			// Show a notification that the request is queued
			pluginLog?.Information($"Your sync request with {targetUserName} has been queued. Data will be delivered automatically when available.");
		}
	}
	catch (Exception ex)
	{
		pluginLog?.Error($"Error handling sync request queued message: {ex.Message}");
		pairResult = $"Error handling queued sync request: {ex.Message}";
	}
}

private string GetPlayerCharacterName()
{
	try
	{
		if (clientState?.LocalPlayer != null)
{
	var player = clientState.LocalPlayer;
	return player.Name.ToString() ?? "Unknown";
}
		else if (clientState?.LocalContentId != 0)
		{
			// Fallback if player object isn't available but we have a content ID
			return $"Player_{clientState.LocalContentId:X}";
		}
		else
		{
			// Final fallback
			return $"Player_{DateTime.Now:HHmmss}";
		}
	}
			catch (Exception ex)
		{
			pluginLog?.Warning($"Failed to get character name: {ex.Message}");
			return $"Player_{DateTime.Now:HHmmss}";
		}
	}
	
	private string GetPlayerUserId()
	{
		// Get the user ID from the network service
		if (networkService != null)
		{
			return networkService.GetCurrentUserId();
		}
		return string.Empty;
	}
	
	private string GetSourceUserId(dynamic characterData)
	{
		try
		{
			// Debug: Log the structure we're receiving
			pluginLog?.Information($"DEBUG: GetSourceUserId called with characterData type: {characterData?.GetType().Name}");
			
			// Try to access the top-level properties
			var availableProperties = new List<string>();
			try
			{
				// Try to enumerate properties to see what's available
				foreach (var prop in characterData.GetType().GetProperties())
				{
					availableProperties.Add(prop.Name);
				}
			}
			catch
			{
				// If we can't enumerate, try direct access
				availableProperties.Add("(direct access only)");
			}
			
			pluginLog?.Information($"DEBUG: Available properties: {string.Join(", ", availableProperties)}");
			
			// Try different possible locations for user_id
			if (characterData?.user_id != null)
			{
				var userId = characterData.user_id.ToString();
				pluginLog?.Information($"Found source user ID at user_id: {userId}");
				return userId;
			}
			
			if (characterData?.source_user_id != null)
			{
				var userId = characterData.source_user_id.ToString();
				pluginLog?.Information($"Found source user ID at source_user_id: {userId}");
				return userId;
			}
			
			if (characterData?.target_user_id != null)
			{
				var userId = characterData.target_user_id.ToString();
				pluginLog?.Information($"Found source user ID at target_user_id: {userId}");
				return userId;
			}
			
			// If not found, try to get it from the online users list by character name
			var sourceCharacterName = characterData?.source_character_name?.ToString();
			if (!string.IsNullOrEmpty(sourceCharacterName))
			{
				pluginLog?.Information($"DEBUG: Looking up user ID for character name: {sourceCharacterName}");
				var userId = GetUserIdFromCharacterName(sourceCharacterName);
				if (!string.IsNullOrEmpty(userId))
				{
					pluginLog?.Information($"Found source user ID from character name: {userId}");
					return userId;
				}
			}
			
			pluginLog?.Warning("Source user ID not found in character data - file metadata fetch may fail");
			pluginLog?.Warning($"DEBUG: Character data structure: {Newtonsoft.Json.JsonConvert.SerializeObject(characterData, Newtonsoft.Json.Formatting.Indented)}");
			return string.Empty;
		}
		catch (Exception ex)
		{
			pluginLog?.Error($"Failed to get source user ID: {ex.Message}");
			return string.Empty;
		}
	}
	
	        private string GetUserIdFromCharacterName(string characterName)
        {
            try
            {
                // Look through the online users list to find the user ID for this character name
                foreach (var user in onlineUsers)
                {
                    if (user?.name?.ToString() == characterName)
                    {
                        return user?.id?.ToString() ?? string.Empty;
                    }
                }
                
                pluginLog?.Warning($"Could not find user ID for character name: {characterName}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Failed to get user ID from character name: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Filters users to only show those who are visually visible to the local player
        /// </summary>
        private List<dynamic> GetVisuallyVisibleUsers(List<dynamic> allUsers)
        {
            try
            {
                if (clientState?.LocalPlayer == null)
                {
                    return new List<dynamic>(); // Return empty list if we can't determine visibility
                }

                // Get list of currently visible player names from object table
                var visiblePlayerNames = GetCurrentlyVisiblePlayerNames();
                if (visiblePlayerNames.Count == 0)
                {
                    return new List<dynamic>();
                }

                var visibleUsers = new List<dynamic>();
                var usersNotVisible = 0;
                
                foreach (var user in allUsers)
                {
                    try
                    {
                        var userName = user?.name?.ToString();
                        if (string.IsNullOrEmpty(userName))
                        {
                            continue;
                        }
                        
                        // Check if this user is currently visible in the game world
                        if (visiblePlayerNames.Contains(userName))
                        {
                            visibleUsers.Add(user);
                        }
                        else
                        {
                            usersNotVisible++;
                        }
                    }
                    catch (Exception ex)
                    {
                        pluginLog?.Warning($"Error processing user {user?.name} for visibility filtering: {ex.Message}");
                        // Skip user on error for safety
                        usersNotVisible++;
                    }
                }

                
                return visibleUsers;
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Error in visibility filtering: {ex.Message}");
                // Return empty list on error for safety
                return new List<dynamic>();
            }
        }
        
        /// <summary>
        /// Gets the names of all players currently visible in the object table
        /// </summary>
        private HashSet<string> GetCurrentlyVisiblePlayerNames()
        {
            var visibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                // Use the ModIntegrationService to get visible players
                if (modIntegrationService == null)
                {
                    return visibleNames;
                }

                // Get the current character cache from ModIntegrationService
                // This will contain all currently visible players
                var characterCache = modIntegrationService.GetPlayerCharacters();
                if (characterCache == null || characterCache.Count == 0)
                {
                    return visibleNames;
                }

                foreach (var character in characterCache)
                {
                    var name = character.Key;
                    if (!string.IsNullOrEmpty(name))
                    {
                        visibleNames.Add(name);
                    }
                }
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Error getting visible player names: {ex.Message}");
            }
            
            return visibleNames;
        }

        // Zone detection and caching
        private string _currentZone = string.Empty;
        private DateTime _lastZoneUpdate = DateTime.MinValue;
        private const int ZONE_UPDATE_COOLDOWN_MS = 5000; // Only update zone every 5 seconds
        
        // Visibility cleanup tracking
        private DateTime _lastVisibilityCleanup = DateTime.MinValue;
        private const int VISIBILITY_CLEANUP_COOLDOWN_MS = 10000; // Check for invisible users every 10 seconds

        /// <summary>
        /// Gets the current zone/area the local player is in with caching and reduced logging
        /// </summary>
        private string GetCurrentZone()
        {
            try
            {
                if (clientState?.LocalPlayer == null)
                {
                    return string.Empty;
                }

                // Check if we need to update the zone (cooldown to reduce logging)
                var now = DateTime.Now;
                if (!string.IsNullOrEmpty(_currentZone) && 
                    (now - _lastZoneUpdate).TotalMilliseconds < ZONE_UPDATE_COOLDOWN_MS)
                {
                    return _currentZone; // Return cached zone
                }


                var territoryType = clientState.TerritoryType;
                if (territoryType != 0)
                {
                    var newZone = $"Zone_{territoryType}";
                    
                    // Only log if zone actually changed
                    if (newZone != _currentZone)
                    {
                        pluginLog?.Information($"Zone changed: {_currentZone} → {newZone}");
                        _currentZone = newZone;
                        _lastZoneUpdate = now;
                        
                        // If we're connected, send updated zone info to server
                        if (networkService?.IsConnected == true)
                        {
                            _ = Task.Run(async () => await SendZoneUpdateAsync(newZone));
                        }
                    }
                    
                    return _currentZone;
                }

                // If no territory type, return empty (will show all users for compatibility)
                if (string.IsNullOrEmpty(_currentZone))
                {
                    pluginLog?.Debug("No territory type available, zone filtering disabled");
                    _currentZone = string.Empty;
                    _lastZoneUpdate = now;
                }
                
                return _currentZone;
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Error getting current zone: {ex.Message}");
                return string.Empty;
            }
        }



        /// <summary>
        /// Debug method to show current visibility information
        /// </summary>
        public void ShowVisibilityDebugInfo()
        {
            try
            {
                var currentZone = GetCurrentZone();
                var localPlayer = clientState?.LocalPlayer;
                var visiblePlayerNames = GetCurrentlyVisiblePlayerNames();
                
                pluginLog?.Information("=== VISIBILITY DEBUG INFO ===");
                pluginLog?.Information($"Current Zone: {currentZone}");
                pluginLog?.Information($"Local Player: {localPlayer?.Name ?? "Unknown"}");
                pluginLog?.Information($"Territory Type: {clientState?.TerritoryType ?? 0}");
                pluginLog?.Information($"Visually Visible Players: {string.Join(", ", visiblePlayerNames)}");
                
                if (onlineUsers?.Count > 0)
                {
                    pluginLog?.Information($"Online Users ({onlineUsers.Count}):");
                    foreach (var user in onlineUsers)
                    {
                        var userName = user?.name?.ToString() ?? "Unknown";
                        var userZone = user?.zone?.ToString() ?? "No Zone Info";
                        var isSameZone = userZone.Equals(currentZone, StringComparison.OrdinalIgnoreCase);
                        var isVisuallyVisible = visiblePlayerNames.Contains(userName);
                        pluginLog?.Information($"  - {userName}: Zone={userZone}, Same Zone={isSameZone}, Visually Visible={isVisuallyVisible}");
                    }
                }
                else
                {
                    pluginLog?.Information("No online users found");
                }
                
                pluginLog?.Information("=== END VISIBILITY DEBUG ===");
            }
            catch (Exception ex)
            {
                pluginLog?.Error($"Error showing visibility debug info: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends zone update to server without full character data
        /// </summary>
        private async Task SendZoneUpdateAsync(string newZone)
        {
            try
            {
                if (networkService?.IsConnected == true)
                {
                    var zoneUpdate = new
                    {
                        type = "zone_update",
                        data = new
                        {
                            zone = newZone,
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    };

                    await networkService.SendMessageAsync(Newtonsoft.Json.JsonConvert.SerializeObject(zoneUpdate));
                    pluginLog?.Debug($"Sent zone update to server: {newZone}");
                }
            }
            catch (Exception ex)
            {
                pluginLog?.Warning($"Failed to send zone update: {ex.Message}");
            }
        }
	
	private async Task DownloadAndApplyFilesAsync(Dictionary<string, object> fileMetadata, string sourceCharacterName)
	{
		try
		{
			if (modIntegrationService == null || receivedModsService == null)
			{
				pluginLog?.Error("Required services not available for file download");
				return;
			}
			
			// Get user ID for progress tracking
			var userId = GetUserIdFromCharacterName(sourceCharacterName);
			
			pluginLog?.Information($"Starting concurrent download and application of {fileMetadata.Count} files from {sourceCharacterName}");
			
			// First pass: parse file metadata and prepare download list
			var downloadFiles = new List<(string hash, string relativePath)>();
			
			foreach (var kvp in fileMetadata)
			{
				try
				{
					var fileKey = kvp.Key;
					var fileInfo = kvp.Value;
					
					// Parse file metadata - handle both JObject and JsonElement
					string? hash = null;
					string? relativePath = null;
					
					if (fileInfo is Newtonsoft.Json.Linq.JObject fileInfoObj)
					{
						// Newtonsoft.Json format
						hash = fileInfoObj["hash"]?.ToString();
						relativePath = fileInfoObj["relative_path"]?.ToString();
					}
					else if (fileInfo is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
					{
						// System.Text.Json format
						if (jsonElement.TryGetProperty("hash", out var hashElement))
							hash = hashElement.GetString();
						if (jsonElement.TryGetProperty("relative_path", out var pathElement))
							relativePath = pathElement.GetString();
					}
					
					if (!string.IsNullOrEmpty(hash) && !string.IsNullOrEmpty(relativePath))
					{
						downloadFiles.Add((hash, relativePath));
					}
					else
					{
						pluginLog?.Warning($"Invalid file metadata for {fileKey}: missing hash or relative_path");
					}
				}
				catch (Exception ex)
				{
					pluginLog?.Error($"Failed to parse file metadata {kvp.Key}: {ex.Message}");
				}
			}
			
			// Second pass: download files concurrently
			if (downloadFiles.Count > 0)
			{
				pluginLog?.Information($"Starting concurrent download of {downloadFiles.Count} files...");
				
				var progress = new Progress<(int completed, int total, string currentFile)>(report =>
				{
					if (!string.IsNullOrEmpty(userId))
					{
						var fileProgress = 0.7f + (0.2f * report.completed / report.total);
						UpdateSyncProgress(userId, $"Downloading {report.currentFile}...", fileProgress);
					}
					pluginLog?.Information($"Download progress: {report.completed}/{report.total} - {report.currentFile}");
				});
				
				var downloadResult = await modIntegrationService.DownloadFilesConcurrentlyAsync(downloadFiles, maxConcurrency: 5, progress);
				
				pluginLog?.Information($"Concurrent download completed: {downloadResult.successCount} successful, {downloadResult.failureCount} failed");
				
				if (downloadResult.errors.Count > 0)
				{
					pluginLog?.Warning($"Download errors: {string.Join(", ", downloadResult.errors.Take(5))}");
				}
				
				// Apply the downloaded files if any were successful
				if (downloadResult.successCount > 0)
				{
					pluginLog?.Information($"Applying {downloadResult.successCount} downloaded files to received mods partition");
					
					// Update progress for file application
					if (!string.IsNullOrEmpty(userId))
					{
						UpdateSyncProgress(userId, "Applying mod files...", 0.9f);
					}
					
					try
					{
						// Get the received mods directory path - use the same path as ModIntegrationService
						var receivedModsPath = modIntegrationService?.GetReceivedModsDirectory();
						if (string.IsNullOrEmpty(receivedModsPath))
						{
							pluginLog?.Error("Received mods path not configured");
							if (!string.IsNullOrEmpty(userId))
							{
								FailSyncProgress(userId, "Received mods path not configured");
							}
							return;
						}
						
						pluginLog?.Information($"Applying {downloadResult.downloadedFilePaths.Count} specific downloaded files to received mods directory: {receivedModsPath}");
						
						// Apply ONLY the specific files that were just downloaded (this prevents applying old/corrupted files)
						pluginLog?.Information($"CRITICAL: About to call ApplyDownloadedFilesToPenumbraAsync for character '{sourceCharacterName}'");
						var applyResult = await modIntegrationService.ApplyDownloadedFilesToPenumbraAsync(receivedModsPath, sourceCharacterName, downloadResult.downloadedFilePaths);
						pluginLog?.Information($"CRITICAL: ApplyDownloadedFilesToPenumbraAsync completed with result: {applyResult}");
						pluginLog?.Information($"File application result: {applyResult}");
						
						// Complete sync progress
						if (!string.IsNullOrEmpty(userId))
						{
							pluginLog?.Information($"CRITICAL: About to complete sync progress for user '{userId}'");
							CompleteSyncProgress(userId);
							pluginLog?.Information($"CRITICAL: Sync progress completed for user '{userId}'");
						}
					}
					catch (Exception ex)
					{
						pluginLog?.Error($"Failed to apply downloaded files: {ex.Message}");
						if (!string.IsNullOrEmpty(userId))
						{
							FailSyncProgress(userId, $"File application error: {ex.Message}");
						}
					}
				}
				else
				{
					// No files downloaded, but sync is complete
					if (!string.IsNullOrEmpty(userId))
					{
						CompleteSyncProgress(userId);
					}
				}
			}
			else
			{
				pluginLog?.Warning($"No valid files found in metadata for {sourceCharacterName}");
				if (!string.IsNullOrEmpty(userId))
				{
					CompleteSyncProgress(userId);
				}
			}
		}
		catch (Exception ex)
		{
			pluginLog?.Error($"Failed to download and apply files: {ex.Message}");
		}
	}

        public override void Draw()
        {
            try
            {
                // Title bar buttons are handled automatically by Dalamud
                // No need for manual header implementation
                
                if (!isConnected)
                {
                    // Disconnected state - show big red status and connect button
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 20);
                    
                    // Big red disconnected status
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    ImGui.PushFont(ImGui.GetIO().Fonts.Fonts[0]); // Use default font for now
                    ImGui.Text("DISCONNECTED");
                    ImGui.PopFont();
                    ImGui.PopStyleColor();
                    
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 20);
                    
                    // Connect button
                    if (ImGui.Button("Connect to Server", new Vector2(200, 30)))
                    {
                        ConnectToServer();
                    }
                    
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Configure connection settings to get started");
                }
                else
                {
                    // Connected state - show sync status and pairs
                    ImGui.Text("Connection Status:");
                    ImGui.SameLine();
                    ImGui.TextColored(statusColor, statusMessage);
                    
                    ImGui.Text("Sync Status:");
                    ImGui.SameLine();
                    var syncColor = isSyncing ? new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(0.8f, 0.8f, 0.8f, 1.0f);
                    ImGui.TextColored(syncColor, syncStatus);
                    
                    // Test info
                    if (!string.IsNullOrEmpty(testInfo))
                    {
                        ImGui.Text("Test Info:");
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), testInfo);
                    }
                    
                    ImGui.Separator();
                    
                    // Local Users section (visually visible only)
                    ImGui.Text("Visible Users:");
                    ImGui.Separator();

// Filter users by visual visibility (only show users who are actually visible in the game world)
                var visibleUsers = GetVisuallyVisibleUsers(onlineUsers);

if (visibleUsers.Count > 0)
{
	foreach (var user in visibleUsers)
	{
		var userName = user?.name?.ToString() ?? "Unknown";
		var userId = user?.id?.ToString() ?? "";
		
		// Skip the current user - don't show pair button for yourself
		if (userId == GetPlayerUserId())
		{
			continue;
		}
		
		// Check if this user has active sync progress
		var hasProgress = userSyncProgress.ContainsKey(userId);
		var progress = hasProgress ? userSyncProgress[userId] : null;
		
		// User name and pair button on same line
		ImGui.Text(userName);
		ImGui.SameLine();
		
		// Pair button (disable if sync is in progress)
		if (hasProgress && progress.IsActive)
		{
			ImGui.BeginDisabled();
		}
		
		var buttonLabel = $"Pair##{userId}";
		if (ImGui.Button(buttonLabel, new Vector2(60, 20)))
		{
			RequestUserData(userId);
		}
		
		if (hasProgress && progress.IsActive)
		{
			ImGui.EndDisabled();
		}
		
		// Show loading bar if sync is in progress
		if (hasProgress)
		{
			ImGui.SameLine();
			ImGui.Text("Syncing...");
			ImGui.Spacing();
			DrawSyncProgressBar(progress);
			ImGui.Spacing();
		}
	}
}
else
{
	ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No users in your current zone");
	ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Other players will appear here when they're nearby");
}

// Show pair result
if (!string.IsNullOrEmpty(pairResult))
{
	ImGui.Text("Pair Result:");
	ImGui.SameLine();
	ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), (ImU8String)pairResult);
}
                    
                    ImGui.Separator();
                    
                    // Mod Integration section
                    ImGui.Text("Mod Integration:");
                    ImGui.Separator();
                    
                    if (modIntegrationService != null)
                    {
                        // Penumbra status
                        var penumbraColor = modIntegrationService.PenumbraAvailable ? 
                            new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                        ImGui.Text("Penumbra:");
                        ImGui.SameLine();
                        ImGui.TextColored(penumbraColor, modIntegrationService.PenumbraAvailable ? "Available" : "Not Available");
                        
                        // Glamourer status
                        var glamourerColor = modIntegrationService.GlamourerAvailable ? 
                            new Vector4(0.0f, 1.0f, 0.0f, 1.0f) : new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
                        ImGui.Text("Glamourer:");
                        ImGui.SameLine();
                        ImGui.TextColored(glamourerColor, modIntegrationService.GlamourerAvailable ? "Available" : "Not Available");
                        
                                                // Save & Send character data button (combines both operations)
                        if (ImGui.Button("Save & Send Character Data", new Vector2(200, 30)))
                        {
                            SaveAndSendCharacterData();
                        }
                        
                        // Force reconnection button (only show when APIs are unavailable)
                        if (!modIntegrationService.PenumbraAvailable || !modIntegrationService.GlamourerAvailable)
                        {
                            if (ImGui.Button("Force Reconnect APIs", new Vector2(200, 30)))
                            {
                                modIntegrationService.ForceReconnection();
                            }
                        }
                        
                        // Show save & send result
                        if (!string.IsNullOrEmpty(sendToServerResult))
                        {
                            ImGui.Text("Save & Send Result:");
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), sendToServerResult);
                        }
                        
                        // Show collected data if available
                        if (showModData)
                        {
                            ImGui.Separator();
                            ImGui.Text("Collected Data:");
                            

                            
                            // Glamourer data
                            if (!string.IsNullOrEmpty(glamourerData))
                            {
                                ImGui.Text("Glamourer Data:");
                                ImGui.SameLine();
                                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Available");
                                
                                if (ImGui.CollapsingHeader("View Glamourer Data"))
                                {
                                    ImGui.TextWrapped(glamourerData);
                                }
                            }
                            
                            // Penumbra data
                            ImGui.Text("Penumbra Data:");
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), penumbraData);
                            
                            // Penumbra meta data
                            if (!string.IsNullOrEmpty(penumbraMetaData))
                            {
                                ImGui.Text("Penumbra Meta:");
                                ImGui.SameLine();
                                ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Available");
                                
                                if (ImGui.CollapsingHeader("View Penumbra Meta Data"))
                                {
                                    ImGui.TextWrapped(penumbraMetaData);
                                }
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Mod integration service not available");
                    }
                    
                    ImGui.Separator();
                    
                    // Disconnect button
                    if (ImGui.Button("Disconnect", new Vector2(200, 30)))
                    {
                        DisconnectFromServer();
                    }
                }
            }
            catch (Exception ex)
            {
                // If UI drawing fails, hide the window to prevent further crashes
                IsOpen = false;
                System.Diagnostics.Debug.WriteLine($"Error drawing PluginUI: {ex.Message}");
            }
        }
    }
}



