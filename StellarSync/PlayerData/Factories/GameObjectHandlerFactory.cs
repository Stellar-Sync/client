using StellarSync.API.Data.Enum;
using StellarSync.PlayerData.Handlers;
using StellarSync.Services;
using StellarSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace StellarSync.PlayerData.Factories;

public class GameObjectHandlerFactory
{
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StellarMediator _stellarMediator;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public GameObjectHandlerFactory(ILoggerFactory loggerFactory, PerformanceCollectorService performanceCollectorService, StellarMediator stellarMediator,
        DalamudUtilService dalamudUtilService)
    {
        _loggerFactory = loggerFactory;
        _performanceCollectorService = performanceCollectorService;
        _stellarMediator = stellarMediator;
        _dalamudUtilService = dalamudUtilService;
    }

    public async Task<GameObjectHandler> Create(ObjectKind objectKind, Func<nint> getAddressFunc, bool isWatched = false)
    {
        return await _dalamudUtilService.RunOnFrameworkThread(() => new GameObjectHandler(_loggerFactory.CreateLogger<GameObjectHandler>(),
            _performanceCollectorService, _stellarMediator, _dalamudUtilService, objectKind, getAddressFunc, isWatched)).ConfigureAwait(false);
    }
}