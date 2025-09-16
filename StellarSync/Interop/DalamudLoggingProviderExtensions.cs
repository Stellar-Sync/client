using Dalamud.Plugin.Services;
using StellarSync.StellarConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace StellarSync.Interop;

public static class DalamudLoggingProviderExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog, bool hasModifiedGameFiles)
    {
        builder.ClearProviders();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>
            (b => new DalamudLoggingProvider(b.GetRequiredService<StellarConfigService>(), pluginLog, hasModifiedGameFiles)));
        return builder;
    }
}