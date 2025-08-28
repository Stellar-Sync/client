using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using StellarSync.Configuration;
using StellarSync.Services;

namespace StellarSync.UI
{
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
        private string applyTestResult = "";
        private string sendToServerResult = "";
        
        // Services
        private NetworkService? networkService;
        private SettingsUI? settingsUI;
        private ModIntegrationService? modIntegrationService;

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
        }

        public void SetNetworkService(NetworkService networkService)
        {
            this.networkService = networkService;
        }

        public void SetSettingsUI(SettingsUI settingsUI)
        {
            this.settingsUI = settingsUI;
        }

        public void SetModIntegrationService(ModIntegrationService modIntegrationService)
        {
            this.modIntegrationService = modIntegrationService;
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
                var serverUrl = settingsUI?.GetServerUrl() ?? "http://localhost:6000";
                var testMode = settingsUI?.GetTestMode() ?? false;
                
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
                    await networkService.ConnectAsync(serverUrl);
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
        
        private async void ApplyStoredData()
        {
            if (modIntegrationService == null) return;
            
            try
            {
                UpdateTestInfo("Applying stored data to character...");
                applyTestResult = await modIntegrationService.ApplyStoredDataToCharacter();
                UpdateTestInfo($"Apply result: {applyTestResult}");
            }
            catch (Exception ex)
            {
                UpdateTestInfo($"Error applying stored data: {ex.Message}");
            }
        }

        private async void SendCharacterDataToServer()
        {
            if (networkService == null || modIntegrationService == null) return;
            
            try
            {
                UpdateTestInfo("Sending character data to server...");
                
                // Create character data object
                var characterData = new
                {
                    glamourer_data = glamourerData,
                    penumbra_meta = penumbraMetaData,
                    penumbra_files = penumbraData,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                await networkService.SendCharacterDataAsync(characterData);
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

        public override void Draw()
        {
            try
            {
                // Header with settings button in top right
                ImGui.Text("Stellar Sync");
                ImGui.SameLine(ImGui.GetWindowWidth() - 80);
                if (ImGui.Button("⚙️", new Vector2(30, 20)))
                {
                    if (settingsUI != null)
                        settingsUI.IsOpen = true;
                }
                ImGui.Separator();
                
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
                    
                    // Pairs section
                    ImGui.Text("Connected Pairs:");
                    ImGui.Separator();
                    
                    // TODO: Show actual pairs here
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No pairs connected yet");
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Other players will appear here when they connect");
                    
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
                        
                        // Save character data button
                        if (ImGui.Button("Save Character Data", new Vector2(200, 30)))
                        {
                            TestCharacterData();
                        }
                        
                        // Apply stored data button
                        if (ImGui.Button("Apply Stored Data", new Vector2(200, 30)))
                        {
                            ApplyStoredData();
                        }
                        
                        // Send to server button (only show when connected and have data)
                        if (networkService != null && networkService.IsConnected && showModData)
                        {
                            if (ImGui.Button("Send to Server", new Vector2(200, 30)))
                            {
                                SendCharacterDataToServer();
                            }
                        }
                        
                        // Show apply result
                        if (!string.IsNullOrEmpty(applyTestResult))
                        {
                            ImGui.Text("Apply Result:");
                            ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), applyTestResult);
                        }
                        
                        // Show send to server result
                        if (!string.IsNullOrEmpty(sendToServerResult))
                        {
                            ImGui.Text("Send Result:");
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
