using StellarSync.StellarConfiguration.Models;

namespace StellarSync.StellarConfiguration.Configurations;

public class ServerTagConfig : IStellarConfiguration
{
    public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}