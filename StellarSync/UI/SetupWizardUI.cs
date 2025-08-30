using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using StellarSync.Configuration;

namespace StellarSync.UI
{
    public class SetupWizardUI : Window
    {
        private readonly Configuration.Configuration _configuration;
        private readonly IPluginLog _logger;
        private readonly FileDialogManager _fileDialogManager;
        private readonly Action _onSetupComplete;
        private string selectedPath = "";
        private string errorMessage = "";
        private bool isPathValid = false;
        private bool hasBeenPositioned = false;
        
        // Storage settings
        private long selectedStorageLimitGB = 20;
        private bool autoDeleteOldMods = true;

        public SetupWizardUI(Configuration.Configuration configuration, IPluginLog logger, FileDialogManager fileDialogManager, Action onSetupComplete) : base("Stellar Sync Setup###StellarSyncSetupWizard")
        {
            _configuration = configuration;
            _logger = logger;
            _fileDialogManager = fileDialogManager;
            _onSetupComplete = onSetupComplete;
            
            // Set window flags - modal but movable
            Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;
            
            // Set size constraints - larger to accommodate storage settings
            SizeConstraints = new WindowSizeConstraints()
            {
                MinimumSize = new Vector2(600, 500),
                MaximumSize = new Vector2(600, 500)
            };
        }

        public override void Draw()
        {
            try
            {
                DrawSetupWizard();
                
                // Draw the file dialog if it's open
                _fileDialogManager.Draw();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error drawing SetupWizardUI: {ex.Message}");
                IsOpen = false;
            }
        }

        private void DrawSetupWizard()
        {
            // Center the window only on first draw
            if (!hasBeenPositioned)
            {
                var center = ImGui.GetIO().DisplaySize * 0.5f;
                ImGui.SetNextWindowPos(center, ImGuiCond.Once, new Vector2(0.5f, 0.5f));
                hasBeenPositioned = true;
            }
            
            ImGui.Text("Welcome to Stellar Sync!");
            ImGui.Separator();
            
            ImGui.Text("Setup Required");
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.0f, 1.0f));
            ImGui.Text("You must complete this setup before using Stellar Sync.");
            ImGui.PopStyleColor();
            
            ImGui.Spacing();
            ImGui.Text("Please set up a directory where received mod files will be stored.");
            ImGui.Spacing();
            
            // Warning section
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.8f, 0.0f, 1.0f));
            ImGui.Text("⚠️  IMPORTANT: Choose a safe location!");
            ImGui.PopStyleColor();
            
            ImGui.Text("DO NOT select:");
            ImGui.BulletText("Final Fantasy XIV installation directory");
            ImGui.BulletText("Square Enix folders");
            ImGui.BulletText("Your Penumbra mods directory");
            ImGui.BulletText("Any game-related folders");
            ImGui.Spacing();
            
            ImGui.Text("RECOMMENDED locations:");
            ImGui.BulletText("Documents folder");
            ImGui.BulletText("Desktop");
            ImGui.BulletText("A dedicated Stellar Sync folder");
            ImGui.Spacing();
            
            ImGui.Separator();
            
            // Path selection
            ImGui.Text("Storage Directory:");
            ImGui.Spacing();
            
            var displayPath = string.IsNullOrEmpty(selectedPath) ? "No directory selected" : selectedPath;
            ImGui.SetNextItemWidth(450);
            ImGui.InputText("##selectedPath", ref displayPath, 512, ImGuiInputTextFlags.ReadOnly);
            ImGui.SameLine();
            
            if (ImGui.Button("Browse", new Vector2(100, 20)))
            {
                OpenFolderDialog();
            }
            
            ImGui.Spacing();
            
            // Path validation
            if (!string.IsNullOrEmpty(selectedPath))
            {
                ValidatePath();
                
                if (isPathValid)
                {
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        // Show warning in yellow
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 1.0f, 0.0f, 1.0f));
                        ImGui.Text(errorMessage);
                        ImGui.PopStyleColor();
                    }
                    else
                    {
                        // Show success in green
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 1.0f, 0.0f, 1.0f));
                        ImGui.Text("✓ Path is valid");
                        ImGui.PopStyleColor();
                    }
                }
                else
                {
                    // Show error in red
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.0f, 0.0f, 1.0f));
                    ImGui.Text($"✗ {errorMessage}");
                    ImGui.PopStyleColor();
                }
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            
            // Storage settings
            ImGui.Text("Storage Settings:");
            ImGui.Spacing();
            
            ImGui.Text("Storage Limit:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.InputScalar("##storageLimitGB", ImGuiDataType.S64, ref selectedStorageLimitGB, IntPtr.Zero, IntPtr.Zero);
            ImGui.SameLine();
            ImGui.Text("GB");
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "This is the maximum amount of space Stellar Sync will use for received mods.");
            
            ImGui.Spacing();
            ImGui.Checkbox("Auto-delete old mods when limit reached", ref autoDeleteOldMods);
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 1.0f, 0.0f, 1.0f), "(Deletes oldest files first)");
            
            ImGui.Spacing();
            ImGui.Separator();
            
            // Buttons - Setup is mandatory, no cancel option
            var buttonWidth = 120f;
            var windowWidth = ImGui.GetWindowWidth();
            var buttonX = (windowWidth - buttonWidth) * 0.5f;
            
            ImGui.SetCursorPosX(buttonX);
            
            if (!isPathValid)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
            }
            
            if (ImGui.Button("Continue", new Vector2(buttonWidth, 30)) && isPathValid)
            {
                SaveConfiguration();
                IsOpen = false;
                
                // Auto-open main UI when setup is complete
                _onSetupComplete?.Invoke();
            }
            
            if (!isPathValid)
            {
                ImGui.PopStyleVar();
            }
            
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "You can change these settings later in the settings after setup is complete.");
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "The main interface will open automatically when setup is complete.");
        }

        private void OpenFolderDialog()
        {
            try
            {
                // Get initial directory - start with Documents if available
                var initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(initialDir) || !Directory.Exists(initialDir))
                {
                    initialDir = "C:\\";
                }

                _fileDialogManager.OpenFolderDialog("Pick Stellar Sync Storage Folder", (success, path) =>
                {
                    if (!success) return;

                    _logger.Information($"Selected folder: {path}");
                    selectedPath = path;
                    ValidatePath();
                }, initialDir);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error opening folder dialog: {ex.Message}");
                errorMessage = $"Failed to open folder dialog: {ex.Message}";
                isPathValid = false;
            }
        }

        private void ValidatePath()
        {
            try
            {
                if (string.IsNullOrEmpty(selectedPath))
                {
                    errorMessage = "Please select a directory";
                    isPathValid = false;
                    return;
                }

                // Check if path exists or can be created
                if (!Directory.Exists(selectedPath))
                {
                    try
                    {
                        Directory.CreateDirectory(selectedPath);
                        _logger.Information($"Created directory: {selectedPath}");
                    }
                    catch (Exception ex)
                    {
                        errorMessage = $"Cannot create directory: {ex.Message}";
                        isPathValid = false;
                        return;
                    }
                }

                // Check for dangerous paths - only warn about actual game directories
                var lowerPath = selectedPath.ToLowerInvariant();
                var warningMessage = "";
                
                // Check for actual FFXIV/SE directories
                if (lowerPath.Contains("final fantasy xiv") || 
                    lowerPath.Contains("ffxiv") ||
                    lowerPath.Contains("square enix"))
                {
                    warningMessage = "⚠️  Warning: This appears to be a Final Fantasy XIV or Square Enix directory. Please consider using a different location to avoid potential conflicts.";
                }
                // Check for Steam game directories
                else if (lowerPath.Contains("steamapps\\common\\final fantasy xiv") ||
                         lowerPath.Contains("steamapps\\common\\ffxiv"))
                {
                    warningMessage = "⚠️  Warning: This appears to be a Final Fantasy XIV Steam installation directory. Please consider using a different location to avoid potential conflicts.";
                }
                // Check for Penumbra directory
                else if (lowerPath.Contains("penumbra") || lowerPath.Contains("xivmods"))
                {
                    warningMessage = "⚠️  Warning: This appears to be a Penumbra mods directory. Please use a separate location to avoid contaminating your mod collection.";
                }

                // Check if we can write to the directory
                try
                {
                    var testFile = Path.Combine(selectedPath, "test_write.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    errorMessage = $"Cannot write to this directory: {ex.Message}";
                    isPathValid = false;
                    return;
                }

                // Path is valid - show warning if any, but don't block
                isPathValid = true;
                errorMessage = warningMessage; // Show warning instead of blocking
                _logger.Information($"Validated directory: {selectedPath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error validating path: {ex.Message}");
                errorMessage = $"Validation error: {ex.Message}";
                isPathValid = false;
            }
        }

        private void SaveConfiguration()
        {
            try
            {
                _configuration.ReceivedModsPath = selectedPath;
                _configuration.MaxReceivedModsSizeGB = selectedStorageLimitGB;
                _configuration.AutoDeleteOldMods = autoDeleteOldMods;
                _configuration.Save();
                _logger.Information($"Saved received mods path: {selectedPath}, storage limit: {selectedStorageLimitGB}GB, auto-delete: {autoDeleteOldMods}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to save configuration: {ex.Message}");
            }
        }
    }
}
