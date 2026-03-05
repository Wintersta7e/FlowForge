using FlowForge.Core.Execution;
using FlowForge.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers FlowForge.Core services: <see cref="NodeRegistry"/> (singleton),
    /// <see cref="PipelineRunner"/> (transient per execution),
    /// and <see cref="AppSettingsManager"/> (singleton).
    /// Requires <see cref="ILoggerFactory"/> to be registered by the host (call <c>AddLogging()</c> first).
    /// </summary>
    public static IServiceCollection AddFlowForgeCore(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
            NodeRegistry.CreateDefault(sp.GetRequiredService<ILoggerFactory>()));
        services.AddTransient<PipelineRunner>();
        services.AddSingleton(sp =>
            new AppSettingsManager(sp.GetRequiredService<ILogger<AppSettingsManager>>()));

        return services;
    }
}
