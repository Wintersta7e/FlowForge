using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Settings;

public class AppSettingsManagerTests
{
    [Fact]
    public async Task Load_nonexistent_file_returns_default_settings()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "nonexistent", "settings.json");

        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);

        AppSettings settings = await manager.LoadAsync();

        settings.RecentPipelines.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_then_load_roundtrip_preserves_all_properties()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);
        var original = new AppSettings
        {
            RecentPipelines = new List<string> { Path.Combine(dir.Path, "pipelines", "test.ffpipe") },
        };

        await manager.SaveAsync(original);
        AppSettings loaded = await manager.LoadAsync();

        loaded.RecentPipelines.Should().Equal(original.RecentPipelines);
    }

    [Fact]
    public async Task Load_corrupt_json_returns_defaults()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{{{{ not valid json !@#$");

        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);

        AppSettings settings = await manager.LoadAsync();

        settings.RecentPipelines.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_null_recent_pipelines_returns_empty_list()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """{"recentPipelines": null}""");

        AppSettings settings = await new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance).LoadAsync();

        settings.RecentPipelines.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task Load_ignores_retired_keys_and_next_save_omits_them()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");
        string recent = Path.Combine(dir.Path, "a.ffpipe");
        string json = JsonSerializer.Serialize(new
        {
            maxConcurrency = 8,
            defaultInputFolder = dir.Path,
            defaultOutputFolder = dir.OutputPath,
            recentPipelines = new[] { recent },
        });
        await File.WriteAllTextAsync(settingsPath, json);
        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);

        AppSettings settings = await manager.LoadAsync();
        settings.RecentPipelines.Should().Equal(recent);
        await manager.SaveAsync(settings);

        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        saved.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("recentPipelines");
    }

    [Fact]
    public async Task Save_creates_parent_directory_if_missing()
    {
        using var dir = new TempDirectory();
        string nestedPath = Path.Combine(dir.Path, "deep", "nested", "settings.json");

        var manager = new AppSettingsManager(nestedPath, NullLogger<AppSettingsManager>.Instance);

        await manager.SaveAsync(new AppSettings());

        File.Exists(nestedPath).Should().BeTrue();
        string? parentDir = Path.GetDirectoryName(nestedPath);
        Directory.Exists(parentDir).Should().BeTrue();
    }

    [Fact]
    public async Task Save_atomic_tmp_file_does_not_exist_after_save()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);

        await manager.SaveAsync(new AppSettings());

        // Verify no temp files of any name were left behind (GUID-named .tmp files)
        Directory.GetFiles(dir.Path, "*.tmp").Should().BeEmpty();
        File.Exists(settingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_with_null_settings_throws_ArgumentNullException()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);

        Func<Task> act = () => manager.SaveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
