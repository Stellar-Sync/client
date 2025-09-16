using StellarSync.API.Data;
using StellarSync.API.Data.Comparer;
using StellarSync.Interop.Ipc;
using StellarSync.StellarConfiguration;
using StellarSync.StellarConfiguration.Models;
using StellarSync.Services.Mediator;
using System.Collections.Concurrent;

namespace StellarSync.PlayerData.Pairs;

public class PluginWarningNotificationService
{
    private readonly ConcurrentDictionary<UserData, OptionalPluginWarning> _cachedOptionalPluginWarnings = new(UserDataComparer.Instance);
    private readonly IpcManager _ipcManager;
    private readonly StellarConfigService _stellarConfigService;
    private readonly StellarMediator _mediator;

    public PluginWarningNotificationService(StellarConfigService stellarConfigService, IpcManager ipcManager, StellarMediator mediator)
    {
        _stellarConfigService = stellarConfigService;
        _ipcManager = ipcManager;
        _mediator = mediator;
    }

    public void NotifyForMissingPlugins(UserData user, string playerName, HashSet<PlayerChanges> changes)
    {
        if (!_cachedOptionalPluginWarnings.TryGetValue(user, out var warning))
        {
            _cachedOptionalPluginWarnings[user] = warning = new()
            {
                ShownCustomizePlusWarning = _stellarConfigService.Current.DisableOptionalPluginWarnings,
                ShownHeelsWarning = _stellarConfigService.Current.DisableOptionalPluginWarnings,
                ShownHonorificWarning = _stellarConfigService.Current.DisableOptionalPluginWarnings,
                ShownMoodlesWarning = _stellarConfigService.Current.DisableOptionalPluginWarnings,
                ShowPetNicknamesWarning = _stellarConfigService.Current.DisableOptionalPluginWarnings
            };
        }

        List<string> missingPluginsForData = [];
        if (changes.Contains(PlayerChanges.Heels) && !warning.ShownHeelsWarning && !_ipcManager.Heels.APIAvailable)
        {
            missingPluginsForData.Add("SimpleHeels");
            warning.ShownHeelsWarning = true;
        }
        if (changes.Contains(PlayerChanges.Customize) && !warning.ShownCustomizePlusWarning && !_ipcManager.CustomizePlus.APIAvailable)
        {
            missingPluginsForData.Add("Customize+");
            warning.ShownCustomizePlusWarning = true;
        }

        if (changes.Contains(PlayerChanges.Honorific) && !warning.ShownHonorificWarning && !_ipcManager.Honorific.APIAvailable)
        {
            missingPluginsForData.Add("Honorific");
            warning.ShownHonorificWarning = true;
        }

        if (changes.Contains(PlayerChanges.Moodles) && !warning.ShownMoodlesWarning && !_ipcManager.Moodles.APIAvailable)
        {
            missingPluginsForData.Add("Moodles");
            warning.ShownMoodlesWarning = true;
        }

        if (changes.Contains(PlayerChanges.PetNames) && !warning.ShowPetNicknamesWarning && !_ipcManager.PetNames.APIAvailable)
        {
            missingPluginsForData.Add("PetNicknames");
            warning.ShowPetNicknamesWarning = true;
        }

        if (missingPluginsForData.Any())
        {
            _mediator.Publish(new NotificationMessage("Missing plugins for " + playerName,
                $"Received data for {playerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.",
                NotificationType.Warning, TimeSpan.FromSeconds(10)));
        }
    }
}