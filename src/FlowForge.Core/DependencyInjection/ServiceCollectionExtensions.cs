using FlowForge.Core.Execution;
using FlowForge.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlowForgeCore(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
            NodeRegistry.CreateDefault(sp.GetRequiredService<ILoggerFactory>()));
        services.AddTransient<PipelineRunner>();
        services.AddSingleton<AppSettingsManager>();

        return services;
    }
}
