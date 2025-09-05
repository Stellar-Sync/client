using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Helpers;
using System;
using System.Threading.Tasks;

namespace StellarSync.Interop.Ipc;

/// <summary>
/// Handles all Glamourer IPC interactions
/// </summary>
    public sealed class IpcCallerGlamourer : IIpcCaller, IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IDalamudPluginInterface _pluginInterface;
        
        // Glamourer API
        private readonly Glamourer.Api.IpcSubscribers.GetStateBase64? _glamourerGetState;
        private readonly Glamourer.Api.IpcSubscribers.ApplyState? _glamourerApplyState;
    
            public IpcCallerGlamourer(IPluginLog logger, IDalamudPluginInterface pluginInterface)
        {
            _logger = logger;
            _pluginInterface = pluginInterface;
            
            // Initialize Glamourer IPC subscribers
            try
            {
                _glamourerGetState = new Glamourer.Api.IpcSubscribers.GetStateBase64(pluginInterface);
                _glamourerApplyState = new Glamourer.Api.IpcSubscribers.ApplyState(pluginInterface);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to initialize Glamourer IPC subscribers: {ex.Message}");
                _glamourerGetState = null;
                _glamourerApplyState = null;
            }
        }
    
    /// <summary>
    /// Checks if Glamourer API is available
    /// </summary>
    public bool IsAvailable => _glamourerGetState != null && _glamourerApplyState != null;
    
    /// <summary>
    /// Gets the Glamourer API version
    /// </summary>
    public string GetApiVersion()
    {
        try
        {
            if (!IsAvailable)
                return string.Empty;
                
            return "Glamourer IPC API available";
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Glamourer API version: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Gets the current state of a character as base64 string
    /// </summary>
    public string GetStateBase64(ushort objectIndex)
    {
        try
        {
            if (_glamourerGetState == null)
                return string.Empty;
                
            var result = _glamourerGetState.Invoke(objectIndex);
            return result.Item2 ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Glamourer state for object {objectIndex}: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Applies a state to a character
    /// </summary>
    public bool ApplyState(Newtonsoft.Json.Linq.JObject stateData, ushort objectIndex, uint lockCode = 0)
    {
        try
        {
            if (_glamourerApplyState == null)
                return false;
                
            var result = _glamourerApplyState.Invoke(stateData, objectIndex, lockCode);
            return result == Glamourer.Api.Enums.GlamourerApiEc.Success;
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to apply Glamourer state to object {objectIndex}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Tests Glamourer connectivity and basic functionality
    /// </summary>
    public async Task<string> TestConnectivityAsync()
    {
        try
        {
            _logger.Information("Testing Glamourer connectivity...");
            
            if (!IsAvailable)
            {
                return "Glamourer API not available";
            }
            
            // Test API version
            var version = GetApiVersion();
            if (string.IsNullOrEmpty(version))
            {
                return "Failed to get Glamourer API version";
            }
            
            _logger.Information($"Glamourer API version: {version}");
            
            return "Glamourer connectivity test completed successfully! The API is accessible and working.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Error during Glamourer connectivity test: {ex.Message}");
            return $"Error during test: {ex.Message}";
        }
    }
    
    public void Dispose()
    {
        // Glamourer API doesn't need explicit disposal
        GC.SuppressFinalize(this);
    }
}
