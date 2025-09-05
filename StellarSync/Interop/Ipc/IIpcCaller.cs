using System.Threading.Tasks;

namespace StellarSync.Interop.Ipc;

/// <summary>
/// Base interface for all IPC callers
/// </summary>
public interface IIpcCaller
{
    /// <summary>
    /// Checks if the mod API is available
    /// </summary>
    bool IsAvailable { get; }
    
    /// <summary>
    /// Tests connectivity to the mod
    /// </summary>
    Task<string> TestConnectivityAsync();
}

