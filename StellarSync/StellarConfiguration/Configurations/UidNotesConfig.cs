using StellarSync.StellarConfiguration.Models;

namespace StellarSync.StellarConfiguration.Configurations;

public class UidNotesConfig : IStellarConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
