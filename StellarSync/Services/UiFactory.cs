using StellarSync.API.Dto.Group;
using StellarSync.PlayerData.Pairs;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using StellarSync.UI;
using StellarSync.UI.Components.Popup;
using StellarSync.WebAPI;
using Microsoft.Extensions.Logging;

namespace StellarSync.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly StellarMediator _stellarMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly StellarProfileManager _stellarProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public UiFactory(ILoggerFactory loggerFactory, StellarMediator stellarMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverConfigManager,
        StellarProfileManager stellarProfileManager, PerformanceCollectorService performanceCollectorService)
    {
        _loggerFactory = loggerFactory;
        _stellarMediator = stellarMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _stellarProfileManager = stellarProfileManager;
        _performanceCollectorService = performanceCollectorService;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _stellarMediator,
            _apiController, _uiSharedService, _pairManager, dto, _performanceCollectorService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _stellarMediator,
            _uiSharedService, _serverConfigManager, _stellarProfileManager, _pairManager, pair, _performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _stellarMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }
}
