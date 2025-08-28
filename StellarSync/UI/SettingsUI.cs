using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using StellarSync.Configuration;
using StellarSync.Services;

namespace StellarSync.UI
{
    public class SettingsUI : Window
    {
        // Configuration fields
        private string serverUrl = "http://localhost:6000";
        private bool autoConnect = false;
        private bool testMode = false;
        
        // Tab management
        private int selectedTab = 0;
        private readonly string[] tabs = { "Connection", "Sync", "Privacy", "Advanced" };

        public SettingsUI() : base("Stellar Sync Settings###StellarSyncSettingsUI")
        {
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
            serverUrl = config.ServerUrl;
            autoConnect = config.AutoConnect;
            testMode = config.TestMode;
        }

        public void SaveConfiguration(Configuration.Configuration config)
        {
            config.ServerUrl = serverUrl;
            config.AutoConnect = autoConnect;
            config.TestMode = testMode;
            config.Save();
        }

        public string GetServerUrl() => serverUrl;
        public bool GetTestMode() => testMode;

        public void SetNetworkService(NetworkService networkService)
        {
            // Settings UI doesn't need direct network service access
            // but we keep this for consistency with the main UI
        }

        public override void Draw()
        {
            try
            {
                ImGui.Text("Stellar Sync Settings");
                ImGui.Separator();
                
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
                
                // Save button
                if (ImGui.Button("Save Settings", new Vector2(120, 30)))
                {
                    // TODO: Save configuration
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
            ImGui.InputText("##serverUrl", ref serverUrl, 256);
            
            ImGui.Spacing();
            ImGui.Checkbox("Auto-connect on startup", ref autoConnect);
            
            ImGui.Spacing();
            ImGui.Checkbox("Enable Test Mode", ref testMode);
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
            
            ImGui.Text("Advanced Options:");
            ImGui.Spacing();
            
            // TODO: Add advanced settings
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Advanced settings will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Debug options will be added here.");
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Performance settings will be added here.");
        }
    }
}
