using System.Text.Json;
using Avalonia.Headless.XUnit;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

[Collection(VisualCollection.Name)]
public sealed class LaunchTests
{
    private readonly ITestOutputHelper _output;

    public LaunchTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public async Task Desktop_composition_resolves_main_view_model_with_temp_settings()
    {
        using var temp = new TempDirectory();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        var services = new ServiceCollection();
        App.ConfigureServices(services);
        services.AddLogging();
        services.AddSingleton(new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance));

        await using (ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
        {
            MainWindowViewModel vm = provider.GetRequiredService<MainWindowViewModel>();
            await vm.InitializeAsync();
            await vm.ClearRecentCommand.ExecuteAsync(null);
        }

        _output.WriteLine($"settings-file={settingsPath} exists={File.Exists(settingsPath)}");
        File.Exists(settingsPath).Should().BeTrue("clearing recent pipelines saves through the injected settings path");
        using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        settings.RootElement.ValueKind.Should().Be(JsonValueKind.Object);

        List<string> callSites = ConfigureServicesCallSites();
        callSites.ForEach(_output.WriteLine);
        _output.WriteLine($"configure-services-call-sites={callSites.Count}");
        callSites.Should().ContainSingle().Which.Should().StartWith("App.axaml.cs:");
    }

    // Every line under src/FlowForge.UI (build output excluded) that calls ConfigureServices, as "file:line".
    private static List<string> ConfigureServicesCallSites()
    {
        string uiRoot = RepoRoot.Combine("src", "FlowForge.UI");
        return Directory.EnumerateFiles(uiRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(uiRoot, file))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(relative => File.ReadLines(Path.Combine(uiRoot, relative))
                .Select((text, index) => (Relative: relative, Line: index + 1, Text: text)))
            .Where(l => l.Text.Contains("ConfigureServices(", StringComparison.Ordinal)
                && !l.Text.Contains("void ConfigureServices(", StringComparison.Ordinal))
            .Select(l => $"{l.Relative.Replace('\\', '/')}:{l.Line}")
            .ToList();
    }
}
