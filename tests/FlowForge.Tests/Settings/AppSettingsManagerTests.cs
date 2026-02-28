using FluentAssertions;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using Serilog;

namespace FlowForge.Tests.Settings;

public class AppSettingsManagerTests
{
    private static readonly ILogger Logger = new LoggerConfiguration().CreateLogger();

    [Fact]
    public async Task Load_nonexistent_file_returns_default_settings()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "nonexistent", "settings.json");

        var manager = new AppSettingsManager(settingsPath, Logger);

        AppSettings settings = await manager.LoadAsync();

        settings.DefaultInputFolder.Should().BeEmpty();
        settings.DefaultOutputFolder.Should().BeEmpty();
        settings.MaxConcurrency.Should().Be(Environment.ProcessorCount);
        settings.RecentPipelines.Should().BeEmpty();
    }

    [Fact]
    public async Task Save_then_load_roundtrip_preserves_all_properties()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, Logger);
        var original = new AppSettings
        {
            DefaultInputFolder = "/home/user/input",
            DefaultOutputFolder = "/home/user/output",
            MaxConcurrency = 4,
            RecentPipelines = new List<string> { "/home/user/pipelines/test.ffpipe" },
        };

        await manager.SaveAsync(original);
        AppSettings loaded = await manager.LoadAsync();

        loaded.DefaultInputFolder.Should().Be(original.DefaultInputFolder);
        loaded.DefaultOutputFolder.Should().Be(original.DefaultOutputFolder);
        loaded.MaxConcurrency.Should().Be(original.MaxConcurrency);
        loaded.RecentPipelines.Should().Equal(original.RecentPipelines);
    }

    [Fact]
    public async Task Load_corrupt_json_returns_defaults()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{{{{ not valid json !@#$");

        var manager = new AppSettingsManager(settingsPath, Logger);

        AppSettings settings = await manager.LoadAsync();

        settings.DefaultInputFolder.Should().BeEmpty();
        settings.MaxConcurrency.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public async Task Save_creates_parent_directory_if_missing()
    {
        using var dir = new TempDirectory();
        string nestedPath = Path.Combine(dir.Path, "deep", "nested", "settings.json");

        var manager = new AppSettingsManager(nestedPath, Logger);

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

        var manager = new AppSettingsManager(settingsPath, Logger);

        await manager.SaveAsync(new AppSettings());

        string tmpPath = settingsPath + ".tmp";
        File.Exists(tmpPath).Should().BeFalse();
        File.Exists(settingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveAsync_with_null_settings_throws_ArgumentNullException()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, Logger);

        Func<Task> act = () => manager.SaveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Custom_settings_values_preserved_through_save_and_load()
    {
        using var dir = new TempDirectory();
        string settingsPath = Path.Combine(dir.Path, "settings.json");

        var manager = new AppSettingsManager(settingsPath, Logger);
        var custom = new AppSettings
        {
            DefaultInputFolder = "/mnt/data/photos",
            DefaultOutputFolder = "/mnt/data/processed",
            MaxConcurrency = 16,
            RecentPipelines = new List<string> { "/mnt/data/pipelines/batch.ffpipe" },
        };

        await manager.SaveAsync(custom);
        AppSettings loaded = await manager.LoadAsync();

        loaded.DefaultInputFolder.Should().Be("/mnt/data/photos");
        loaded.DefaultOutputFolder.Should().Be("/mnt/data/processed");
        loaded.MaxConcurrency.Should().Be(16);
        loaded.RecentPipelines.Should().Equal("/mnt/data/pipelines/batch.ffpipe");
    }
}
