using StellarSync.API.Dto.User;
using StellarSync.PlayerData.Pairs;
using StellarSync.Services.Mediator;
using StellarSync.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;

namespace StellarSync.PlayerData.Factories;

public class PairFactory
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly StellarMediator _stellarMediator;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory,
        StellarMediator stellarMediator, ServerConfigurationManager serverConfigurationManager)
    {
        _loggerFactory = loggerFactory;
        _cachedPlayerFactory = cachedPlayerFactory;
        _stellarMediator = stellarMediator;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create(UserFullPairDto userPairDto)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), userPairDto, _cachedPlayerFactory, _stellarMediator, _serverConfigurationManager);
    }

    public Pair Create(UserPairDto userPairDto)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), new(userPairDto.User, userPairDto.IndividualPairStatus, [], userPairDto.OwnPermissions, userPairDto.OtherPermissions),
            _cachedPlayerFactory, _stellarMediator, _serverConfigurationManager);
    }
}