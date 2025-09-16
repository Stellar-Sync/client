using StellarSync.StellarConfiguration.Configurations;

namespace StellarSync.StellarConfiguration;

public class StellarConfigService : ConfigurationServiceBase<StellarConfig>
{
    public const string ConfigName = "config.json";

    public StellarConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}