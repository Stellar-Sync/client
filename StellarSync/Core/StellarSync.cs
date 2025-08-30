using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.ImGuiFileDialog;
using StellarSync.UI;
using StellarSync.Services;
using StellarSync.Configuration;

namespace StellarSync
{
    public sealed class StellarSync : IDalamudPlugin
    {
        public string Name => "Stellar Sync";
        private const string CommandName = "/stellar";
        private const string SettingsCommandName = "/stellarsettings";

        private readonly IDalamudPluginInterface PluginInterface;
        private readonly ICommandManager CommandManager;
        private readonly IClientState ClientState;
        private readonly IObjectTable ObjectTable;
        private readonly IGameGui GameGui;
        private readonly IPluginLog PluginLog;
        private readonly IChatGui ChatGui;
        private readonly IFramework Framework;
        private readonly WindowSystem WindowSystem;
        private readonly PluginUI PluginUi;
        private readonly SettingsUI SettingsUi;
        private readonly Configuration.Configuration Configuration;
        private readonly NetworkService NetworkService;
        private readonly ModIntegrationService ModIntegrationService;
        private readonly CharacterSyncService CharacterSyncService;
        private readonly ReceivedModsService ReceivedModsService;
        private readonly SetupWizardUI SetupWizardUI;
        private readonly FileDialogManager FileDialogManager;

        public StellarSync(IDalamudPluginInterface pluginInterface, ICommandManager commandManager,
            IClientState clientState, IObjectTable objectTable, IGameGui gameGui, IPluginLog pluginLog, IChatGui chatGui, IFramework framework)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ClientState = clientState;
            this.ObjectTable = objectTable;
            this.GameGui = gameGui;
            this.PluginLog = pluginLog;
            this.ChatGui = chatGui;
            this.Framework = framework;
            
            // Initialize configuration - load from Dalamud's config system
            this.Configuration = pluginInterface.GetPluginConfig() as Configuration.Configuration ?? new Configuration.Configuration();
            this.Configuration.Initialize(pluginInterface);
            
            // Create services
            this.NetworkService = new NetworkService();
            this.ModIntegrationService = new ModIntegrationService(PluginLog, PluginInterface, this.Configuration, ObjectTable, ClientState, Framework);
            this.ReceivedModsService = new ReceivedModsService(this.Configuration, PluginLog);
            this.CharacterSyncService = new CharacterSyncService(this.NetworkService, this.Configuration, 
                this.ClientState, this.ObjectTable, this.ModIntegrationService);
            
            // Create file dialog manager
            this.FileDialogManager = new FileDialogManager();
            
            // Create setup wizard with callback to open main UI when complete
            this.SetupWizardUI = new SetupWizardUI(this.Configuration, PluginLog, this.FileDialogManager, () => 
            {
                PluginLog?.Information("Setup completed. Opening main UI.");
                ChatGui?.Print("[Stellar Sync] Setup completed! Opening main interface.");
                PluginUi.IsOpen = true;
            });
            
            // Create window system and UI
            this.WindowSystem = new WindowSystem("StellarSync");
            this.PluginUi = new PluginUI();
            this.SettingsUi = new SettingsUI(this.FileDialogManager);
            
            // Wire up services to UI
this.PluginUi.SetNetworkService(this.NetworkService);
this.PluginUi.SetSettingsUI(this.SettingsUi);
this.PluginUi.SetModIntegrationService(this.ModIntegrationService);
this.PluginUi.SetCharacterSyncService(this.CharacterSyncService);
this.PluginUi.SetReceivedModsService(this.ReceivedModsService);
this.PluginUi.SetPluginLog(this.PluginLog);
this.PluginUi.SetClientState(this.ClientState);
this.SettingsUi.SetNetworkService(this.NetworkService);
            
            // Load configuration into UI
            this.SettingsUi.LoadConfiguration(this.Configuration);
            
            // Add windows to the system
            this.WindowSystem.AddWindow(this.PluginUi);
            this.WindowSystem.AddWindow(this.SettingsUi);
            this.WindowSystem.AddWindow(this.SetupWizardUI);

            // Subscribe to network events
            this.NetworkService.Connected += OnNetworkConnected;
            this.NetworkService.Disconnected += OnNetworkDisconnected;
            this.NetworkService.ErrorOccurred += OnNetworkError;
            
            // Subscribe to character sync events
            this.CharacterSyncService.PlayerJoined += OnPlayerJoined;
            this.CharacterSyncService.PlayerLeft += OnPlayerLeft;
            this.CharacterSyncService.SyncError += OnSyncError;

            // Log that the plugin is starting
            PluginLog?.Information("Stellar Sync plugin is starting...");
            
            // Check if setup is needed
            CheckAndShowSetupWizard();
            
            ChatGui?.Print("[Stellar Sync] Plugin loaded successfully!");

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Stellar Sync - Character synchronization plugin"
            });

            CommandManager.AddHandler(SettingsCommandName, new CommandInfo(OnSettingsCommand)
            {
                HelpMessage = "Stellar Sync Settings - Open configuration window"
            });

            // Register UI callbacks to fix validation errors
            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            PluginInterface.UiBuilder.OpenMainUi += OpenMainUI;
        }

        public void Dispose()
        {
            try
            {
                PluginLog?.Information("Stellar Sync plugin is shutting down...");
                
                // Unsubscribe from events
                this.NetworkService.Connected -= OnNetworkConnected;
                this.NetworkService.Disconnected -= OnNetworkDisconnected;
                this.NetworkService.ErrorOccurred -= OnNetworkError;
                
                this.CharacterSyncService.PlayerJoined -= OnPlayerJoined;
                this.CharacterSyncService.PlayerLeft -= OnPlayerLeft;
                this.CharacterSyncService.SyncError -= OnSyncError;
                
                // Dispose services
                this.CharacterSyncService?.Dispose();
                this.NetworkService?.Dispose();
                
                WindowSystem?.RemoveAllWindows();
                CommandManager.RemoveHandler(CommandName);
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error during plugin disposal: {ex.Message}");
            }
        }

        private void OnCommand(string command, string args)
        {
            try
            {
                PluginLog?.Information($"Stellar Sync command executed with args: {args}");
                ChatGui?.Print($"[Stellar Sync] Command executed! Args: {args}");
                
                // Check if setup is required first
                if (string.IsNullOrEmpty(Configuration.ReceivedModsPath))
                {
                    PluginLog?.Information("Setup required. Opening setup wizard instead of main UI.");
                    ChatGui?.Print("[Stellar Sync] Setup required. Please complete the setup wizard first.");
                    SetupWizardUI.IsOpen = true;
                    return;
                }
                
                // Open the UI when command is executed
                PluginUi.IsOpen = true;
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error executing command: {ex.Message}");
                ChatGui?.Print($"[Stellar Sync] Error: {ex.Message}");
            }
        }

        private void OnSettingsCommand(string command, string args)
        {
            try
            {
                PluginLog?.Information($"Stellar Sync Settings command executed with args: {args}");
                ChatGui?.Print($"[Stellar Sync] Settings command executed! Args: {args}");
                
                // Open the settings UI
                SettingsUi.IsOpen = true;
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error executing settings command: {ex.Message}");
                ChatGui?.Print($"[Stellar Sync] Error: {ex.Message}");
            }
        }
        
        private void CheckAndShowSetupWizard()
        {
            try
            {
                // Check if received mods path is configured
                if (string.IsNullOrEmpty(Configuration.ReceivedModsPath))
                {
                    PluginLog?.Information("No received mods directory configured. Showing setup wizard...");
                    SetupWizardUI.IsOpen = true;
                }
                else
                {
                    // Verify the configured path is still valid
                    try
                    {
                        var testPath = ReceivedModsService.GetReceivedModsDirectory();
                        PluginLog?.Information($"Using configured received mods directory: {testPath}");
                    }
                    catch (Exception ex)
                    {
                        PluginLog?.Warning($"Configured received mods directory is invalid: {ex.Message}. Showing setup wizard...");
                        SetupWizardUI.IsOpen = true;
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error checking setup status: {ex.Message}");
            }
        }

        private void DrawUI()
        {
            try
            {
                WindowSystem?.Draw();
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error drawing UI: {ex.Message}");
            }
        }

        private void DrawConfigUI()
        {
            try
            {
                // Check if setup is required first
                if (string.IsNullOrEmpty(Configuration.ReceivedModsPath))
                {
                    PluginLog?.Information("Setup required. Opening setup wizard instead of settings.");
                    SetupWizardUI.IsOpen = true;
                    return;
                }
                
                SettingsUi.IsOpen = true;
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error opening config UI: {ex.Message}");
            }
        }

        private void OpenMainUI()
        {
            try
            {
                // Check if setup is required first
                if (string.IsNullOrEmpty(Configuration.ReceivedModsPath))
                {
                    PluginLog?.Information("Setup required. Opening setup wizard instead of main UI.");
                    SetupWizardUI.IsOpen = true;
                    return;
                }
                
                PluginUi.IsOpen = true;
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error opening main UI: {ex.Message}");
            }
        }

        private void OnNetworkConnected(object? sender, EventArgs e)
        {
            try
            {
                PluginLog?.Information("Network connected");
                ChatGui?.Print("[Stellar Sync] Connected to server!");
                PluginUi.UpdateConnectionStatus(true, "Connected");
                PluginUi.UpdateSyncStatus(true, "Syncing active");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling network connection: {ex.Message}");
            }
        }

        private void OnNetworkDisconnected(object? sender, EventArgs e)
        {
            try
            {
                PluginLog?.Information("Network disconnected");
                ChatGui?.Print("[Stellar Sync] Disconnected from server.");
                PluginUi.UpdateConnectionStatus(false, "Disconnected");
                PluginUi.UpdateSyncStatus(false, "Not syncing");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling network disconnection: {ex.Message}");
            }
        }

        private void OnNetworkError(object? sender, string error)
        {
            try
            {
                PluginLog?.Error($"Network error: {error}");
                ChatGui?.Print($"[Stellar Sync] Network error: {error}");
                PluginUi.UpdateConnectionStatus(false, $"Error: {error}");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling network error: {ex.Message}");
            }
        }

        private void OnSyncError(object? sender, string error)
        {
            try
            {
                PluginLog?.Error($"Sync error: {error}");
                ChatGui?.Print($"[Stellar Sync] Sync error: {error}");
                PluginUi.UpdateSyncStatus(false, $"Error: {error}");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling sync error: {ex.Message}");
            }
        }

        private void OnPlayerJoined(object? sender, string playerName)
        {
            try
            {
                PluginLog?.Information($"Player joined: {playerName}");
                ChatGui?.Print($"[Stellar Sync] Player joined: {playerName}");
                PluginUi.UpdateSyncStatus(true, $"Syncing with {playerName}");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling player joined: {ex.Message}");
            }
        }

        private void OnPlayerLeft(object? sender, string playerName)
        {
            try
            {
                PluginLog?.Information($"Player left: {playerName}");
                ChatGui?.Print($"[Stellar Sync] Player left: {playerName}");
                PluginUi.UpdateSyncStatus(true, "Syncing active");
            }
            catch (Exception ex)
            {
                PluginLog?.Error($"Error handling player left: {ex.Message}");
            }
        }
    }
}
