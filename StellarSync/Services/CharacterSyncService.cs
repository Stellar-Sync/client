using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using StellarSync.Configuration;
using StellarSync.Models;

namespace StellarSync.Services
{
    public class CharacterSyncService : IDisposable
    {
        private readonly NetworkService _networkService;
        private readonly Configuration.Configuration _configuration;
        private readonly IClientState? _clientState;
        private readonly IObjectTable? _objectTable;
        private readonly ModIntegrationService _modIntegrationService;

        public event EventHandler<string>? PlayerJoined;
        public event EventHandler<string>? PlayerLeft;
        public event EventHandler<string>? SyncError;

        public CharacterSyncService(NetworkService networkService, Configuration.Configuration configuration, 
            IClientState? clientState = null, IObjectTable? objectTable = null, ModIntegrationService modIntegrationService = null!)
        {
            _networkService = networkService;
            _configuration = configuration;
            _clientState = clientState;
            _objectTable = objectTable;
            _modIntegrationService = modIntegrationService;
        }

        public async Task<CharacterData> GetPlayerCharacterData()
        {
            var characterData = new CharacterData();

            try
            {
                // Get basic player info
                if (_clientState?.LocalPlayer != null)
                {
                    characterData.Name = _clientState.LocalPlayer.Name.TextValue;
                    characterData.World = "Unknown"; // Simplified for now
                    characterData.Position = _clientState.LocalPlayer.Position;
                    characterData.Rotation = _clientState.LocalPlayer.Rotation;
                    characterData.ModelId = 1; // Simplified for now
                }

                // Get equipment data
                characterData.Equipment = GetEquipmentData();

                // Get mod data if available
                if (_modIntegrationService != null)
                {
                    await GetModData(characterData);
                }

                return characterData;
            }
            catch (Exception ex)
            {
                SyncError?.Invoke(this, $"Error getting character data: {ex.Message}");
                return characterData;
            }
        }

        private async Task GetModData(CharacterData characterData)
        {
            try
            {
                SyncError?.Invoke(this, "DEBUG: GetModData method called");
                
                // Get player address
                var playerAddress = _clientState?.LocalPlayer?.Address ?? IntPtr.Zero;
                if (playerAddress == IntPtr.Zero) 
                {
                    SyncError?.Invoke(this, "DEBUG: Player address is zero, returning");
                    return;
                }

                // Get Glamourer data
                if (_modIntegrationService.GlamourerAvailable)
                {
                    var glamourerData = await _modIntegrationService.GetGlamourerDataAsync(playerAddress);
                    characterData.GlamourerData = glamourerData;
                }

                // Get Penumbra data
if (_modIntegrationService.PenumbraAvailable)
{
	SyncError?.Invoke(this, "Penumbra is available, getting data...");
	
	var penumbraData = await _modIntegrationService.GetPenumbraDataAsync(playerAddress);
	characterData.PenumbraData = penumbraData;
	
	SyncError?.Invoke(this, $"Penumbra data retrieved: {penumbraData.Count} categories");
	
	var metaManipulations = _modIntegrationService.GetPenumbraMetaManipulations();
	characterData.PenumbraMetaManipulations = metaManipulations;
	
	// Get Penumbra files for transfer
	if (penumbraData.Count > 0)
	{
		SyncError?.Invoke(this, $"Getting Penumbra files for transfer from {penumbraData.Count} categories...");
		characterData.PenumbraFiles = await _modIntegrationService.GetPenumbraFilesForTransfer(penumbraData);
		SyncError?.Invoke(this, $"Penumbra files prepared: {characterData.PenumbraFiles.Count} files");
		
		// Debug: Log the actual PenumbraFiles dictionary
		if (characterData.PenumbraFiles != null)
		{
			SyncError?.Invoke(this, $"DEBUG: PenumbraFiles dictionary created with {characterData.PenumbraFiles.Count} entries");
			foreach (var kvp in characterData.PenumbraFiles.Take(3))
			{
				SyncError?.Invoke(this, $"DEBUG: File {kvp.Key}: {kvp.Value.Length} bytes");
			}
		}
		else
		{
			SyncError?.Invoke(this, "DEBUG: PenumbraFiles is null!");
		}
	}
	else
	{
		SyncError?.Invoke(this, "No Penumbra data found, skipping file transfer");
	}
}
else
{
	SyncError?.Invoke(this, "Penumbra is not available");
}
            }
            catch (Exception ex)
            {
                SyncError?.Invoke(this, $"Error getting mod data: {ex.Message}");
            }
        }

        private Dictionary<string, object> GetEquipmentData()
        {
            var equipment = new Dictionary<string, object>();

            try
            {
                if (_clientState?.LocalPlayer != null)
                {
                    // Get equipment slots - simplified for now
                    // TODO: Implement proper equipment retrieval when API is available
                    equipment["MainHand"] = 0;
                    equipment["OffHand"] = 0;
                    equipment["Head"] = 0;
                    equipment["Body"] = 0;
                    equipment["Hands"] = 0;
                    equipment["Legs"] = 0;
                    equipment["Feet"] = 0;
                    equipment["Earrings"] = 0;
                    equipment["Necklace"] = 0;
                    equipment["Bracelets"] = 0;
                    equipment["Ring1"] = 0;
                    equipment["Ring2"] = 0;
                }
            }
            catch (Exception ex)
            {
                SyncError?.Invoke(this, $"Error getting equipment data: {ex.Message}");
            }

            return equipment;
        }

        public void Dispose()
        {
            // Clean up any resources
        }
    }
}
