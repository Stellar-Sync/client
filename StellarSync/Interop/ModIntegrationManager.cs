using StellarSync.Interop.Ipc;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StellarSync.Interop;

/// <summary>
/// Manages all mod integrations through individual IPC callers
/// </summary>
    public sealed class ModIntegrationManager : IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IpcCallerPenumbra _penumbraCaller;
        private readonly IpcCallerGlamourer _glamourerCaller;
        
        public ModIntegrationManager(IPluginLog logger, IDalamudPluginInterface pluginInterface)
    {
        _logger = logger;
        _penumbraCaller = new IpcCallerPenumbra(logger, pluginInterface);
        _glamourerCaller = new IpcCallerGlamourer(logger, pluginInterface);
    }
    
    /// <summary>
    /// Gets the Penumbra IPC caller
    /// </summary>
    public IpcCallerPenumbra Penumbra => _penumbraCaller;
    
    /// <summary>
    /// Gets the Glamourer IPC caller
    /// </summary>
    public IpcCallerGlamourer Glamourer => _glamourerCaller;
    
    /// <summary>
    /// Checks if Penumbra is available
    /// </summary>
    public bool PenumbraAvailable => _penumbraCaller.IsAvailable;
    
    /// <summary>
    /// Checks if Glamourer is available
    /// </summary>
    public bool GlamourerAvailable => _glamourerCaller.IsAvailable;
    
    /// <summary>
    /// Tests connectivity to all available mods
    /// </summary>
    public async Task<string> TestAllModsConnectivityAsync()
    {
        var results = new List<string>();
        
        if (PenumbraAvailable)
        {
            var penumbraResult = await _penumbraCaller.TestConnectivityAsync();
            results.Add($"Penumbra: {penumbraResult}");
        }
        else
        {
            results.Add("Penumbra: Not available");
        }
        
        if (GlamourerAvailable)
        {
            var glamourerResult = await _glamourerCaller.TestConnectivityAsync();
            results.Add($"Glamourer: {glamourerResult}");
        }
        else
        {
            results.Add("Glamourer: Not available");
        }
        
        return string.Join("\n", results);
    }
    
    /// <summary>
    /// Gets a summary of all available mods
    /// </summary>
    public string GetModsSummary()
    {
        var mods = new List<string>();
        
        if (PenumbraAvailable)
            mods.Add("Penumbra");
            
        if (GlamourerAvailable)
            mods.Add("Glamourer");
            
        if (mods.Count == 0)
            return "No mods available";
            
        return $"Available mods: {string.Join(", ", mods)}";
    }
    
    public void Dispose()
    {
        _penumbraCaller?.Dispose();
        _glamourerCaller?.Dispose();
        GC.SuppressFinalize(this);
    }
}
