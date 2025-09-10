using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using StellarSync.Configuration;
using StellarSync.Services;

namespace StellarSync.UI
{
    public class SettingsUI : Window
    {
        // Configuration fields
        private string serverUrl = "wss://stellar.kasu.network";
        private bool autoConnect = false;
        private bool testMode = false;
        
        // Received mods partition settings
        private string receivedModsPath = "";
        private long maxReceivedModsSizeGB = 20;
        private bool autoDeleteOldMods = true;
        
        // Tab management
        private int selectedTab = 0;
        private readonly string[] tabs = { "Connection", "Sync", "Privacy", "Advanced" };
        
        // File dialog manager
        private readonly FileDialogManager _fileDialogManager;
        
        // Configuration reference
        private Configuration.Configuration? _configuration;
        
        // Services for testing functionality
        private ModIntegrationService? _modIntegrationService;
        private PluginUI? _mainUI;

        public SettingsUI(FileDialogManager fileDialogManager) : base("Stellar Sync Settings###StellarSyncSettingsUI")
        {
            _fileDialogManager = fileDialogManager;
            
            // Set window flags
            Flags = ImGuiWindowFlags.AlwaysAutoResize;
            
            // Set size constraints
            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(450, 400),
                MaximumSize = new Vector2(800, 600)
            };
        }

        public void LoadConfiguration(Configuration.Configuration config)
        {
            _configuration = config;
            serverUrl = config.ServerUrl;
            autoConnect = config.AutoConnect;
            testMode = config.TestMode;
            
            // Load received mods partition settings
            receivedModsPath = config.ReceivedModsPath;
            maxReceivedModsSizeGB = config.MaxReceivedModsSizeGB;
            autoDeleteOldMods = config.AutoDeleteOldMods;
        }

        public void SaveConfiguration(Configuration.Configuration config)
        {
            config.ServerUrl = serverUrl;
            config.AutoConnect = autoConnect;
            config.TestMode = testMode;
            
            // Save received mods partition settings
            config.ReceivedModsPath = receivedModsPath;
            config.MaxReceivedModsSizeGB = maxReceivedModsSizeGB;
            config.AutoDeleteOldMods = autoDeleteOldMods;
            
            config.Save();
        }

        public string GetServerUrl() => serverUrl;
        public bool GetTestMode() => testMode;

        public void SetNetworkService(NetworkService networkService)
        {
            // Settings UI doesn't need direct network service access
            // but we keep this for consistency with the main UI
        }
        
        public void SetModIntegrationService(ModIntegrationService modIntegrationService)
        {
            _modIntegrationService = modIntegrationService;
        }

        public void SetMainUI(PluginUI mainUI)
        {
            _mainUI = mainUI;
        }

        public override void Draw()
        {
            try
            {
                // Window title already shows "Stellar Sync Settings", no need for duplicate header
                
                // Tab bar
                if (ImGui.BeginTabBar("SettingsTabs"))
                {
                    for (int i = 0; i < tabs.Length; i++)
                    {
                        if (ImGui.BeginTabItem(tabs[i]))
                        {
                            selectedTab = i;
                            DrawTabContent(i);
                            ImGui.EndTabItem();
                        }
                    }
                    ImGui.EndTabBar();
                }
                
                ImGui.Separator();
                
                // Draw the file dialog if it's open
                _fileDialogManager.Draw();
                
                // Save button
                if (ImGui.Button("Save Settings", new Vector2(120, 30)))
                {
                    if (_configuration != null)
                    {
                        SaveConfiguration(_configuration);
                    }
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Close", new Vector2(80, 30)))
                {
                    IsOpen = false;
                }
            }
            catch (Exception ex)
            {
                IsOpen = false;
                System.Diagnostics.Debug.WriteLine($"Error drawing SettingsUI: {ex.Message}");
            }
        }

        private void DrawTabContent(int tabIndex)
        {
            switch (tabIndex)
            {
                case 0: // Connection
                    DrawConnectionTab();
                    break;
                case 1: // Sync
                    DrawSyncTab();
                    break;
                case 2: // Privacy
                    DrawPrivacyTab();
                    break;
                case 3: // Advanced
                    DrawAdvancedTab();
                    break;
            }
        }

        private void DrawConnectionTab()
        {
            ImGui.Text("Connection Settings");
            ImGui.Separator();
            
            ImGui.Text("Server Configuration:");
            ImGui.Spacing();
            
            ImGui.Text("Server URL:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(300);
            if (ImGui.InputText("##serverUrl", ref serverUrl, 256))
            {
                // Auto-save when URL changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            
            ImGui.Spacing();
            if (ImGui.Checkbox("Auto-connect on startup", ref autoConnect))
            {
                // Auto-save when checkbox changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            
            ImGui.Spacing();
            if (ImGui.Checkbox("Enable Test Mode", ref testMode))
            {
                // Auto-save when checkbox changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            if (testMode)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "(Bypasses server)");
            }
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Test Mode allows you to test the plugin without a server running.");
        }

        private void DrawSyncTab()
        {
            ImGui.Text("Synchronization Settings");
            ImGui.Separator();
            
            ImGui.Text("Sync Options:");
            ImGui.Spacing();
            
            // TODO: Add sync settings
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Character sync settings will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Equipment sync settings will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Glamour sync settings will be added here.");
        }

        private void DrawPrivacyTab()
        {
            ImGui.Text("Privacy Settings");
            ImGui.Separator();
            
            ImGui.Text("Privacy Options:");
            ImGui.Spacing();
            
            // TODO: Add privacy settings
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Privacy settings will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Who can see your character settings will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Who you can see settings will be added here.");
        }

        private void DrawAdvancedTab()
        {
            ImGui.Text("Advanced Settings");
            ImGui.Separator();
            
            ImGui.Text("Received Mods Partition:");
            ImGui.Separator();
            
            ImGui.Text("Storage Directory:");
            ImGui.Spacing();
            ImGui.SetNextItemWidth(400);
            if (ImGui.InputText("##receivedModsPath", ref receivedModsPath, 512))
            {
                // Auto-save when path changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Browse", new Vector2(80, 20)))
            {
                OpenFolderDialog();
            }
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Leave empty to use default location (plugin directory)");
            
            ImGui.Spacing();
            ImGui.Text("Storage Limit:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.InputScalar("##maxSizeGB", ImGuiDataType.S64, ref maxReceivedModsSizeGB, IntPtr.Zero, IntPtr.Zero))
            {
                // Auto-save when size changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            ImGui.SameLine();
            ImGui.Text("GB");
            
            ImGui.Spacing();
            if (ImGui.Checkbox("Auto-delete old mods when limit reached", ref autoDeleteOldMods))
            {
                // Auto-save when checkbox changes
                if (_configuration != null)
                {
                    SaveConfiguration(_configuration);
                }
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "(Deletes oldest files first)");
            

            
            ImGui.Separator();
            ImGui.Text("Debug Options:");
            ImGui.Spacing();
            
            // Penumbra Connectivity Test Button
            if (ImGui.Button("Test Penumbra Connectivity", new Vector2(250, 30)))
            {
                _ = Task.Run(async () =>
                {
                    if (_modIntegrationService != null)
                    {
                        var result = await _modIntegrationService.TestPenumbraConnectivityAsync();
                        // Note: In a real implementation, you might want to show this result in the UI
                        // For now, we'll just log it
                        System.Diagnostics.Debug.WriteLine($"Penumbra Connectivity Test Result: {result}");
                    }
                });
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Test Penumbra API connectivity and functionality.\n\nThis will:\n• Check if Penumbra mod directory is accessible\n• Test meta manipulation access\n• Verify resource path functionality\n• Validate API connectivity\n\nSafe to use - won't affect your current collection or settings.");
            }
            
            ImGui.Spacing();
            
            // Penumbra Temporary Collection Test Button
            if (ImGui.Button("Test Penumbra Temp Collection", new Vector2(250, 30)))
            {
                _ = Task.Run(async () =>
                {
                    if (_modIntegrationService != null)
                    {
                        var result = await _modIntegrationService.TestTemporaryCollectionAsync();
                        // Note: In a real implementation, you might want to show this result in the UI
                        // For now, we'll just log it
                        System.Diagnostics.Debug.WriteLine($"Penumbra Temp Collection Test Result: {result}");
                    }
                });
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Test Penumbra temporary collection functionality.\n\nThis will:\n• Create a test temporary collection\n• Assign it to your character\n• Verify the assignment works\n• Clean up the test collection\n\nSafe to use - creates and removes a test collection without affecting your current setup.");
            }
            
            ImGui.Spacing();
            
            // Zone Debug Button
            if (ImGui.Button("Debug Zone Info", new Vector2(250, 30)))
            {
                // Call the main UI's zone debug method
                if (_mainUI != null)
                {
                    _mainUI.ShowVisibilityDebugInfo();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Zone Debug button clicked - but main UI not available");
                }
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Debug zone detection and filtering.\n\nThis will:\n• Show current detected zone\n• Display zone filtering results\n• Help troubleshoot zone-related issues\n\nUseful for debugging why users might not appear in the same zone.");
            }
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "These buttons test Penumbra API functionality.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Performance settings will be added here.");
        }
        
        private void OpenFolderDialog()
        {
            try
            {
                // Get initial directory - start with current path if set, otherwise Documents
                var initialDir = !string.IsNullOrEmpty(receivedModsPath) && Directory.Exists(receivedModsPath) 
                    ? receivedModsPath 
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                
                if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
                {
                    initialDir = "C:\\";
                }

                _fileDialogManager.OpenFolderDialog("Pick Received Mods Storage Folder", (success, path) =>
                {
                    if (!success) return;

                    receivedModsPath = path;
                    
                    // Auto-save when folder is selected
                    if (_configuration != null)
                    {
                        SaveConfiguration(_configuration);
                    }
                }, initialDir);
            }
            catch (Exception ex)
            {
                // Log error but don't show UI error since this is settings
                System.Diagnostics.Debug.WriteLine($"Error opening folder dialog: {ex.Message}");
            }
        }
    }
}
