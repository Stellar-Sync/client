using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
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
        private readonly WindowSystem WindowSystem;
        private readonly PluginUI PluginUi;
        private readonly SettingsUI SettingsUi;
        private readonly Configuration.Configuration Configuration;
        private readonly NetworkService NetworkService;
        private readonly ModIntegrationService ModIntegrationService;
        private readonly CharacterSyncService CharacterSyncService;

        public StellarSync(IDalamudPluginInterface pluginInterface, ICommandManager commandManager,
            IClientState clientState, IObjectTable objectTable, IGameGui gameGui, IPluginLog pluginLog, IChatGui chatGui)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;
            this.ClientState = clientState;
            this.ObjectTable = objectTable;
            this.GameGui = gameGui;
            this.PluginLog = pluginLog;
            this.ChatGui = chatGui;
            
            // Initialize configuration
            this.Configuration = new Configuration.Configuration();
            this.Configuration.Initialize(pluginInterface);
            
            // Create services
            this.NetworkService = new NetworkService();
            this.ModIntegrationService = new ModIntegrationService(PluginLog, PluginInterface);
            this.CharacterSyncService = new CharacterSyncService(this.NetworkService, this.Configuration, 
                this.ClientState, this.ObjectTable, this.ModIntegrationService);
            
            // Create window system and UI
            this.WindowSystem = new WindowSystem("StellarSync");
            this.PluginUi = new PluginUI();
            this.SettingsUi = new SettingsUI();
            
            // Wire up services to UI
this.PluginUi.SetNetworkService(this.NetworkService);
this.PluginUi.SetSettingsUI(this.SettingsUi);
this.PluginUi.SetModIntegrationService(this.ModIntegrationService);
this.PluginUi.SetPluginLog(this.PluginLog);
this.PluginUi.SetClientState(this.ClientState);
this.SettingsUi.SetNetworkService(this.NetworkService);
            
            // Load configuration into UI
            this.SettingsUi.LoadConfiguration(this.Configuration);
            
            // Add windows to the system
            this.WindowSystem.AddWindow(this.PluginUi);
            this.WindowSystem.AddWindow(this.SettingsUi);

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
                PluginUi.IsOpen = true;
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
