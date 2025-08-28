using Dalamud.Configuration;
using Dalamud.Plugin;
using System;
using System.Numerics;

namespace StellarSync.Configuration
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        // Connection settings
        private string _serverUrl = "http://localhost:6000";
        private bool _autoConnect = false;
        private bool _testMode = false;

        // UI settings
        private bool _showUI = true;
        private Vector2 _uiWindowPosition = new Vector2(100, 100);
        private Vector2 _uiWindowSize = new Vector2(400, 300);

        // Sync settings
        private bool _enableCharacterSync = true;
        private bool _enableGearSync = true;
        private bool _enableGlamourSync = true;
        private bool _enableEmoteSync = true;

        // Privacy settings
        private bool _allowOthersToSeeMe = true;
        private bool _showOthersToMe = true;

        public string ServerUrl { get => _serverUrl; set => _serverUrl = value; }
        public bool AutoConnect { get => _autoConnect; set => _autoConnect = value; }
        public bool TestMode { get => _testMode; set => _testMode = value; }
        public bool ShowUI { get => _showUI; set => _showUI = value; }
        public Vector2 UIWindowPosition { get => _uiWindowPosition; set => _uiWindowPosition = value; }
        public Vector2 UIWindowSize { get => _uiWindowSize; set => _uiWindowSize = value; }
        public bool EnableCharacterSync { get => _enableCharacterSync; set => _enableCharacterSync = value; }
        public bool EnableGearSync { get => _enableGearSync; set => _enableGearSync = value; }
        public bool EnableGlamourSync { get => _enableGlamourSync; set => _enableGlamourSync = value; }
        public bool EnableEmoteSync { get => _enableEmoteSync; set => _enableEmoteSync = value; }
        public bool AllowOthersToSeeMe { get => _allowOthersToSeeMe; set => _allowOthersToSeeMe = value; }
        public bool ShowOthersToMe { get => _showOthersToMe; set => _showOthersToMe = value; }

        // IDalamudPluginInterface is not serializable, so we need to not include it
        [NonSerialized]
        private IDalamudPluginInterface? PluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
        }

        public void Save()
        {
            this.PluginInterface?.SavePluginConfig(this);
        }
    }
}
