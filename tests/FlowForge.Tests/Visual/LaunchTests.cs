using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // Removed, not just overridden: the default manager writes the user's real settings file.
        services.RemoveAll<AppSettingsManager>();
        services.AddSingleton(new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance));
        int settingsRegistrations = services.Count(d => d.ServiceType == typeof(AppSettingsManager));
        _output.WriteLine($"settings-registrations={settingsRegistrations}");
        settingsRegistrations.Should().Be(1, "only the temp-path settings manager may be resolvable");

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

    [AvaloniaFact]
    public void Launch_shows_themed_empty_editor()
    {
        using var session = VisualSession.Open(_output);
        using WriteableBitmap frame = session.Capture(VisualFrames.Launch);

        int colours = session.CountDistinctColours(frame);
        string? configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _output.WriteLine($"Application = {Application.Current?.GetType().FullName}");
        _output.WriteLine($"lifetime = {Application.Current?.ApplicationLifetime?.GetType().Name ?? "null"}");
        _output.WriteLine($"frame = {frame.PixelSize.Width}x{frame.PixelSize.Height} colours={colours}");
        _output.WriteLine($"settings = {session.SettingsPath}");
        if (OperatingSystem.IsLinux())
        {
            _output.WriteLine($"XDG_CONFIG_HOME = {configHome}");
        }

        session.ViewModel.Editor.Nodes.Should().BeEmpty();
        session.Window.IsVisible.Should().BeTrue();
        EmptyStateOverlay(session.Window).IsEffectivelyVisible.Should().BeTrue();
        frame.PixelSize.Should().Be(new PixelSize(1400, 820));
        colours.Should().BeGreaterThanOrEqualTo(500, "the themed window paints far more than a blank surface");
        Application.Current.Should().BeOfType<App>();
        Application.Current!.ApplicationLifetime.Should().BeNull();
        session.SettingsPath.Should().StartWith(session.Temp.Path + Path.DirectorySeparatorChar);
        if (OperatingSystem.IsLinux())
        {
            Directory.Exists(configHome).Should().BeTrue("the session points XDG_CONFIG_HOME at a created directory");
            Path.GetFullPath(configHome!).Should().StartWith(Path.GetTempPath());
        }
    }

    // The canvas overlay shown while the editor has no nodes: the nearest border around its headline.
    private static Border EmptyStateOverlay(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => string.Equals(t.Text, "Build your pipeline", StringComparison.Ordinal))
            .FindAncestorOfType<Border>()
            ?? throw new InvalidOperationException("The empty-state headline has no border around it.");

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
