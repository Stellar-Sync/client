using StellarSync.FileCache;
using StellarSync.Interop.Ipc;
using StellarSync.PlayerData.Handlers;
using StellarSync.PlayerData.Pairs;
using StellarSync.Services;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StellarSync.PlayerData.Factories;

public class PairHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StellarMediator _stellarMediator;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;

    public PairHandlerFactory(ILoggerFactory loggerFactory, GameObjectHandlerFactory gameObjectHandlerFactory, IpcManager ipcManager,
        FileDownloadManagerFactory fileDownloadManagerFactory, DalamudUtilService dalamudUtilService,
        PluginWarningNotificationService pluginWarningNotificationManager, IHostApplicationLifetime hostApplicationLifetime,
        FileCacheManager fileCacheManager, StellarMediator stellarMediator, PlayerPerformanceService playerPerformanceService,
        ServerConfigurationManager serverConfigManager)
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _dalamudUtilService = dalamudUtilService;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _hostApplicationLifetime = hostApplicationLifetime;
        _fileCacheManager = fileCacheManager;
        _stellarMediator = stellarMediator;
        _playerPerformanceService = playerPerformanceService;
        _serverConfigManager = serverConfigManager;
    }

    public PairHandler Create(Pair pair)
    {
        return new PairHandler(_loggerFactory.CreateLogger<PairHandler>(), pair, _gameObjectHandlerFactory,
            _ipcManager, _fileDownloadManagerFactory.Create(), _pluginWarningNotificationManager, _dalamudUtilService, _hostApplicationLifetime,
            _fileCacheManager, _stellarMediator, _playerPerformanceService, _serverConfigManager);
    }
}