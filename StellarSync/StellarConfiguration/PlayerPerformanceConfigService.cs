using StellarSync.StellarConfiguration.Configurations;

namespace StellarSync.StellarConfiguration;

public class PlayerPerformanceConfigService : ConfigurationServiceBase<PlayerPerformanceConfig>
{
    public const string ConfigName = "playerperformance.json";
    public PlayerPerformanceConfigService(string configDir) : base(configDir) { }

    public override string ConfigurationName => ConfigName;
}