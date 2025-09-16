using Dalamud.Game.Command;
using Dalamud.Plugin.Services;
using StellarSync.FileCache;
using StellarSync.StellarConfiguration;
using StellarSync.StellarConfiguration.Models;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using StellarSync.UI;
using StellarSync.WebAPI;
using System.Globalization;

namespace StellarSync.Services;

public sealed class CommandManagerService : IDisposable
{
    private const string _commandName = "/stellar";

    private readonly ApiController _apiController;
    private readonly ICommandManager _commandManager;
    private readonly StellarMediator _mediator;
    private readonly StellarConfigService _stellarConfigService;
    private readonly PerformanceCollectorService _performanceCollectorService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public CommandManagerService(ICommandManager commandManager, PerformanceCollectorService performanceCollectorService,
        ServerConfigurationManager serverConfigurationManager, CacheMonitor periodicFileScanner,
        ApiController apiController, StellarMediator mediator, StellarConfigService stellarConfigService)
    {
        _commandManager = commandManager;
        _performanceCollectorService = performanceCollectorService;
        _serverConfigurationManager = serverConfigurationManager;
        _cacheMonitor = periodicFileScanner;
        _apiController = apiController;
        _mediator = mediator;
        _stellarConfigService = stellarConfigService;
        _commandManager.AddHandler(_commandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Stellar Sync UI" + Environment.NewLine + Environment.NewLine +
                "Additionally possible commands:" + Environment.NewLine +
                "\t /stellar toggle - Disconnects from Stellar, if connected. Connects to Stellar, if disconnected" + Environment.NewLine +
                "\t /stellar toggle on|off - Connects or disconnects to Stellar respectively" + Environment.NewLine +
                "\t /stellar gpose - Opens the Stellar Character Data Hub window" + Environment.NewLine +
                "\t /stellar analyze - Opens the Stellar Character Data Analysis window" + Environment.NewLine +
                "\t /stellar settings - Opens the Stellar Settings window"
        });
    }

    public void Dispose()
    {
        _commandManager.RemoveHandler(_commandName);
    }

    private void OnCommand(string command, string args)
    {
        var splitArgs = args.ToLowerInvariant().Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries);

        if (splitArgs.Length == 0)
        {
            // Interpret this as toggling the UI
            if (_stellarConfigService.Current.HasValidSetup())
                _mediator.Publish(new UiToggleMessage(typeof(CompactUi)));
            else
                _mediator.Publish(new UiToggleMessage(typeof(IntroUi)));
            return;
        }

        if (!_stellarConfigService.Current.HasValidSetup())
            return;

        if (string.Equals(splitArgs[0], "toggle", StringComparison.OrdinalIgnoreCase))
        {
            if (_apiController.ServerState == WebAPI.SignalR.Utils.ServerState.Disconnecting)
            {
                _mediator.Publish(new NotificationMessage("Stellar disconnecting", "Cannot use /toggle while Stellar Sync is still disconnecting",
                    NotificationType.Error));
            }

            if (_serverConfigurationManager.CurrentServer == null) return;
            var fullPause = splitArgs.Length > 1 ? splitArgs[1] switch
            {
                "on" => false,
                "off" => true,
                _ => !_serverConfigurationManager.CurrentServer.FullPause,
            } : !_serverConfigurationManager.CurrentServer.FullPause;

            if (fullPause != _serverConfigurationManager.CurrentServer.FullPause)
            {
                _serverConfigurationManager.CurrentServer.FullPause = fullPause;
                _serverConfigurationManager.Save();
                _ = _apiController.CreateConnectionsAsync();
            }
        }
        else if (string.Equals(splitArgs[0], "gpose", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(CharaDataHubUi)));
        }
        else if (string.Equals(splitArgs[0], "rescan", StringComparison.OrdinalIgnoreCase))
        {
            _cacheMonitor.InvokeScan();
        }
        else if (string.Equals(splitArgs[0], "perf", StringComparison.OrdinalIgnoreCase))
        {
            if (splitArgs.Length > 1 && int.TryParse(splitArgs[1], CultureInfo.InvariantCulture, out var limitBySeconds))
            {
                _performanceCollectorService.PrintPerformanceStats(limitBySeconds);
            }
            else
            {
                _performanceCollectorService.PrintPerformanceStats();
            }
        }
        else if (string.Equals(splitArgs[0], "medi", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.PrintSubscriberInfo();
        }
        else if (string.Equals(splitArgs[0], "analyze", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(DataAnalysisUi)));
        }
        else if (string.Equals(splitArgs[0], "settings", StringComparison.OrdinalIgnoreCase))
        {
            _mediator.Publish(new UiToggleMessage(typeof(SettingsUi)));
        }
    }
}