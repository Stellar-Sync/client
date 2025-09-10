using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StellarSync.Interop.Ipc;

/// <summary>
/// Handles all Penumbra IPC interactions
/// </summary>
    public sealed class IpcCallerPenumbra : IIpcCaller, IDisposable
    {
        private readonly IPluginLog _logger;
        private readonly IDalamudPluginInterface _pluginInterface;
    
    // IPC Subscribers
    private readonly GetModDirectory _penumbraGetModDirectory;
    private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;
    private readonly GetGameObjectResourcePaths _penumbraResourcePaths;
    private readonly CreateTemporaryCollection _penumbraCreateTemporaryCollection;
    private readonly DeleteTemporaryCollection _penumbraDeleteTemporaryCollection;
    private readonly AssignTemporaryCollection _penumbraAssignTemporaryCollection;
    private readonly AddTemporaryMod _penumbraAddTemporaryMod;
    private readonly RemoveTemporaryMod _penumbraRemoveTemporaryMod;
        
        /// <summary>
        /// Checks if Penumbra API is available
        /// </summary>
        public bool IsAvailable => _penumbraGetModDirectory != null;
    
            public IpcCallerPenumbra(IPluginLog logger, IDalamudPluginInterface pluginInterface)
    {
        _logger = logger;
        _pluginInterface = pluginInterface;
        
        // Initialize IPC subscribers
        _penumbraGetModDirectory = new GetModDirectory(pluginInterface);
        _penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pluginInterface);
        _penumbraResourcePaths = new GetGameObjectResourcePaths(pluginInterface);
        _penumbraCreateTemporaryCollection = new CreateTemporaryCollection(pluginInterface);
        _penumbraDeleteTemporaryCollection = new DeleteTemporaryCollection(pluginInterface);
        _penumbraAssignTemporaryCollection = new AssignTemporaryCollection(pluginInterface);
        _penumbraAddTemporaryMod = new AddTemporaryMod(pluginInterface);
        _penumbraRemoveTemporaryMod = new RemoveTemporaryMod(pluginInterface);
    }
    
    /// <summary>
    /// Gets the Penumbra mod directory
    /// </summary>
    public string GetModDirectory()
    {
        try
        {
            return _penumbraGetModDirectory.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Penumbra mod directory: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Gets player meta manipulations
    /// </summary>
    public string GetMetaManipulations()
    {
        try
        {
            return _penumbraGetMetaManipulations.Invoke();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Penumbra meta manipulations: {ex.Message}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// Gets resource paths for a game object
    /// </summary>
    public string[] GetGameObjectResourcePaths(ushort objectIndex)
    {
        try
        {
            var result = _penumbraResourcePaths.Invoke(objectIndex);
            // The result is an array of dictionaries, we need to extract the paths
            if (result != null && result.Length > 0 && result[0] != null)
            {
                return result[0].Keys.ToArray();
            }
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to get Penumbra resource paths for object {objectIndex}: {ex.Message}");
            return Array.Empty<string>();
        }
    }
    
    /// <summary>
    /// Creates a temporary collection (LopClient-style method signature)
    /// </summary>
    public PenumbraApiEc CreateTemporaryCollection(string identity, string name, out Guid collectionId)
    {
        try
        {
            // Use the custom Invoke method with out parameter (exactly like LopClient)
            return _penumbraCreateTemporaryCollection.Invoke(identity, name, out collectionId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create Penumbra temporary collection: {ex.Message}");
            collectionId = Guid.Empty;
            return PenumbraApiEc.InvalidArgument;
        }
    }
    
    /// <summary>
    /// Creates a temporary collection (returns tuple for convenience)
    /// </summary>
    public (PenumbraApiEc, Guid) CreateTemporaryCollectionTuple(string identity, string name)
    {
        try
        {
            var ec = CreateTemporaryCollection(identity, name, out var collectionId);
            return (ec, collectionId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create Penumbra temporary collection: {ex.Message}");
            return (PenumbraApiEc.InvalidArgument, Guid.Empty);
        }
    }
    
    
    /// <summary>
    /// Deletes a temporary collection
    /// </summary>
    public PenumbraApiEc DeleteTemporaryCollection(Guid collectionId)
    {
        try
        {
            return _penumbraDeleteTemporaryCollection.Invoke(collectionId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to delete Penumbra temporary collection {collectionId}: {ex.Message}");
            return PenumbraApiEc.InvalidArgument;
        }
    }
    
    /// <summary>
    /// Removes a temporary mod from a collection
    /// </summary>
    public PenumbraApiEc RemoveTemporaryMod(string modName, Guid collectionId, int priority)
    {
        try
        {
            return _penumbraRemoveTemporaryMod.Invoke(modName, collectionId, priority);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to remove temporary mod {modName} from collection {collectionId}: {ex.Message}");
            return PenumbraApiEc.InvalidArgument;
        }
    }
    
    /// <summary>
    /// Adds a temporary mod to a collection
    /// </summary>
    public PenumbraApiEc AddTemporaryMod(string modName, Guid collectionId, Dictionary<string, string> modPaths, string manipulationData, int priority)
    {
        try
        {
            return _penumbraAddTemporaryMod.Invoke(modName, collectionId, modPaths, manipulationData, priority);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to add temporary mod {modName} to collection {collectionId}: {ex.Message}");
            return PenumbraApiEc.InvalidArgument;
        }
    }
    
    /// <summary>
    /// Assigns a temporary collection to an actor
    /// </summary>
    public PenumbraApiEc AssignTemporaryCollection(Guid collectionId, int actorIndex, bool forceAssignment)
    {
        try
        {
            return _penumbraAssignTemporaryCollection.Invoke(collectionId, actorIndex, forceAssignment);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to assign Penumbra temporary collection {collectionId} to actor {actorIndex}: {ex.Message}");
            return PenumbraApiEc.InvalidArgument;
        }
    }
    
    /// <summary>
    /// Sets temporary mods for a collection (removes existing and adds new ones in a single framework thread call)
    /// This prevents race conditions that can occur when doing remove + add separately
    /// </summary>
    public void SetTemporaryMods(Guid applicationId, Guid collectionId, Dictionary<string, string> modPaths)
    {
        if (!IsAvailable) return;

        foreach (var mod in modPaths)
        {
            _logger.Verbose($"[{applicationId}] Change: {mod.Key} => {mod.Value}");
        }
        
        // Remove existing mod first
        var retRemove = _penumbraRemoveTemporaryMod.Invoke("StellarChara_Files", collectionId, 0);
        _logger.Verbose($"[{applicationId}] Removing temp files mod for {collectionId}, Success: {retRemove}");
        
        // Add new mods
        var retAdd = _penumbraAddTemporaryMod.Invoke("StellarChara_Files", collectionId, modPaths, string.Empty, 0);
        _logger.Verbose($"[{applicationId}] Setting temp files mod for {collectionId}, Success: {retAdd}");
        
        // Log any errors
        if (retRemove != PenumbraApiEc.Success)
        {
            _logger.Warning($"[{applicationId}] Warning: RemoveTemporaryMod returned {retRemove}");
        }
        if (retAdd != PenumbraApiEc.Success)
        {
            _logger.Error($"[{applicationId}] Error: AddTemporaryMod returned {retAdd}");
        }
    }
    
    /// <summary>
    /// Tests Penumbra connectivity and basic functionality
    /// </summary>
    public async Task<string> TestConnectivityAsync()
    {
        try
        {
            _logger.Information("Testing Penumbra connectivity...");
            
            // Test mod directory access
            var modDirectory = GetModDirectory();
            if (string.IsNullOrEmpty(modDirectory))
            {
                return "Penumbra mod directory not accessible - API may not be fully initialized";
            }
            
            _logger.Information($"Penumbra mod directory accessible: {modDirectory}");
            
            // Test meta manipulations
            var metaData = GetMetaManipulations();
            _logger.Information($"Meta manipulations accessible: {!string.IsNullOrEmpty(metaData)}");
            
            return "Penumbra connectivity test completed successfully! The API is accessible and working.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Error during Penumbra connectivity test: {ex.Message}");
            return $"Error during test: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Creates a temporary collection (LopClient-style implementation)
    /// </summary>
    public async Task<Guid> CreateTemporaryCollectionAsync(string identity, string uid)
    {
        try
        {
            var collName = identity.Substring(0, Math.Min(identity.Length, 8)) + "_" + uid;
            var ec = CreateTemporaryCollection(identity, collName, out var collId);
            
            if (ec == PenumbraApiEc.Success)
            {
                _logger.Information($"Successfully created temporary collection: {collName} with ID: {collId}");
                return collId;
            }
            else
            {
                _logger.Warning($"Failed to create temporary collection: {ec}");
                return Guid.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error creating temporary collection: {ex.Message}");
            return Guid.Empty;
        }
    }
    
    /// <summary>
    /// Assigns a temporary collection to an actor (LopClient-style implementation)
    /// </summary>
    public async Task AssignTemporaryCollectionAsync(Guid collectionId, int objectIndex)
    {
        try
        {
            var result = AssignTemporaryCollection(collectionId, objectIndex, true);
            _logger.Information($"Assigned temporary collection {collectionId} to object {objectIndex}: {result}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error assigning temporary collection: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Removes a temporary collection (LopClient-style implementation)
    /// </summary>
    public async Task RemoveTemporaryCollectionAsync(Guid collectionId)
    {
        try
        {
            var result = DeleteTemporaryCollection(collectionId);
            _logger.Information($"Removed temporary collection {collectionId}: {result}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Error removing temporary collection: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sets manipulation data for a temporary collection
    /// </summary>
    public async Task SetManipulationDataAsync(Guid applicationId, Guid collId, string manipulationData)
    {
        if (!IsAvailable) return;

        try
        {
            _logger.Information($"[{applicationId}] Setting manipulation data for collection {collId}");
            _logger.Debug($"[{applicationId}] Manipulation data: {manipulationData?.Substring(0, Math.Min(100, manipulationData?.Length ?? 0))}...");
            
            var retAdd = _penumbraAddTemporaryMod.Invoke("StellarChara_Meta", collId, new Dictionary<string, string>(), manipulationData ?? string.Empty, 0);
            _logger.Information($"[{applicationId}] Setting temp meta mod for {collId}, Success: {retAdd}");
        }
        catch (Exception ex)
        {
            _logger.Error($"[{applicationId}] Error setting manipulation data: {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        // IPC subscribers don't need explicit disposal
        GC.SuppressFinalize(this);
    }
}
