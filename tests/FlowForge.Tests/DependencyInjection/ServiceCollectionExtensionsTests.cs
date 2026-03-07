using FlowForge.Core.DependencyInjection;
using FlowForge.Core.Execution;
using FlowForge.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowForge.Tests.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlowForgeCore();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddFlowForgeCore_Resolves_NodeRegistry()
    {
        using ServiceProvider sp = BuildProvider();

        NodeRegistry registry = sp.GetRequiredService<NodeRegistry>();

        registry.Should().NotBeNull();
    }

    [Fact]
    public void AddFlowForgeCore_NodeRegistry_Is_Singleton()
    {
        using ServiceProvider sp = BuildProvider();

        NodeRegistry first = sp.GetRequiredService<NodeRegistry>();
        NodeRegistry second = sp.GetRequiredService<NodeRegistry>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddFlowForgeCore_Resolves_PipelineRunner()
    {
        using ServiceProvider sp = BuildProvider();

        PipelineRunner runner = sp.GetRequiredService<PipelineRunner>();

        runner.Should().NotBeNull();
    }

    [Fact]
    public void AddFlowForgeCore_PipelineRunner_Is_Transient()
    {
        using ServiceProvider sp = BuildProvider();

        PipelineRunner first = sp.GetRequiredService<PipelineRunner>();
        PipelineRunner second = sp.GetRequiredService<PipelineRunner>();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void AddFlowForgeCore_Resolves_AppSettingsManager()
    {
        using ServiceProvider sp = BuildProvider();

        AppSettingsManager manager = sp.GetRequiredService<AppSettingsManager>();

        manager.Should().NotBeNull();
    }

    [Fact]
    public void AddFlowForgeCore_AppSettingsManager_Is_Singleton()
    {
        using ServiceProvider sp = BuildProvider();

        AppSettingsManager first = sp.GetRequiredService<AppSettingsManager>();
        AppSettingsManager second = sp.GetRequiredService<AppSettingsManager>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddFlowForgeCore_Without_Logging_Throws()
    {
        var services = new ServiceCollection();
        services.AddFlowForgeCore();
        using ServiceProvider sp = services.BuildServiceProvider();

        Action act = () => sp.GetRequiredService<NodeRegistry>();

        act.Should().Throw<InvalidOperationException>();
    }
}
