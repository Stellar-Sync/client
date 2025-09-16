using StellarSync.StellarConfiguration.Configurations;

namespace StellarSync.StellarConfiguration;

public interface IConfigService<out T> : IDisposable where T : IStellarConfiguration
{
    T Current { get; }
    string ConfigurationName { get; }
    string ConfigurationPath { get; }
    public event EventHandler? ConfigSave;
    void UpdateLastWriteTime();
}
