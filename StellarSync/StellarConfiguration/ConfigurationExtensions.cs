using StellarSync.StellarConfiguration.Configurations;

namespace StellarSync.StellarConfiguration;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this StellarConfig configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder);
    }
}