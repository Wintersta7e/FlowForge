using FlowForge.Core.Settings;
using FluentAssertions;

namespace FlowForge.Tests.Settings;

public class AppSettingsRecentTests
{
    [Fact]
    public void AddRecentPipeline_adds_to_front()
    {
        var settings = new AppSettings();
        settings.AddRecentPipeline("/path/a.ffpipe");
        settings.AddRecentPipeline("/path/b.ffpipe");

        settings.RecentPipelines.Should().Equal("/path/b.ffpipe", "/path/a.ffpipe");
    }

    [Fact]
    public void AddRecentPipeline_deduplicates_existing_entry()
    {
        var settings = new AppSettings();
        settings.AddRecentPipeline("/path/a.ffpipe");
        settings.AddRecentPipeline("/path/b.ffpipe");
        settings.AddRecentPipeline("/path/a.ffpipe");

        settings.RecentPipelines.Should().Equal("/path/a.ffpipe", "/path/b.ffpipe");
    }

    [Fact]
    public void AddRecentPipeline_trims_to_max_10()
    {
        var settings = new AppSettings();
        for (int i = 0; i < 12; i++)
        {
            settings.AddRecentPipeline($"/path/{i}.ffpipe");
        }

        settings.RecentPipelines.Should().HaveCount(10);
        settings.RecentPipelines[0].Should().Be("/path/11.ffpipe");
    }

    [Fact]
    public void ClearRecentPipelines_empties_list()
    {
        var settings = new AppSettings();
        settings.AddRecentPipeline("/path/a.ffpipe");
        settings.ClearRecentPipelines();

        settings.RecentPipelines.Should().BeEmpty();
    }

    [Fact]
    public void AddRecentPipeline_null_throws_ArgumentException()
    {
        var settings = new AppSettings();
        Action act = () => settings.AddRecentPipeline(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddRecentPipeline_empty_throws_ArgumentException()
    {
        var settings = new AppSettings();
        Action act = () => settings.AddRecentPipeline(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddRecentPipeline_deduplicates_case_insensitively()
    {
        var settings = new AppSettings();
        settings.AddRecentPipeline("C:\\Pipelines\\Test.ffpipe");
        settings.AddRecentPipeline("c:\\pipelines\\test.ffpipe");

        settings.RecentPipelines.Should().HaveCount(1);
    }

    [Fact]
    public void Validate_clamps_invalid_MaxConcurrency()
    {
        var settings = new AppSettings { MaxConcurrency = int.MaxValue };
        settings.Validate();

        settings.MaxConcurrency.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Validate_clamps_zero_MaxConcurrency()
    {
        var settings = new AppSettings { MaxConcurrency = 0 };
        settings.Validate();

        settings.MaxConcurrency.Should().Be(Environment.ProcessorCount);
    }

    [Fact]
    public void Validate_keeps_valid_MaxConcurrency()
    {
        var settings = new AppSettings { MaxConcurrency = 4 };
        settings.Validate();

        settings.MaxConcurrency.Should().Be(4);
    }

    [Fact]
    public void Validate_removes_relative_path_from_RecentPipelines()
    {
        var settings = new AppSettings
        {
            RecentPipelines = new List<string> { "relative/path.ffpipe", Path.Combine(Path.GetTempPath(), "valid.ffpipe") }
        };
        settings.Validate();

        settings.RecentPipelines.Should().ContainSingle()
            .Which.Should().Contain("valid.ffpipe");
    }

    [Fact]
    public void Validate_removes_null_byte_path_from_RecentPipelines()
    {
        var settings = new AppSettings
        {
            RecentPipelines = new List<string> { Path.Combine(Path.GetTempPath(), "ok.ffpipe"), "/tmp/evil\0.ffpipe" }
        };
        settings.Validate();

        settings.RecentPipelines.Should().ContainSingle()
            .Which.Should().Contain("ok.ffpipe");
    }

    [Fact]
    public void Validate_removes_excessively_long_path_from_RecentPipelines()
    {
        var settings = new AppSettings
        {
            RecentPipelines = new List<string>
            {
                Path.Combine(Path.GetTempPath(), "good.ffpipe"),
                "/" + new string('a', 5000) + ".ffpipe"
            }
        };
        settings.Validate();

        settings.RecentPipelines.Should().ContainSingle()
            .Which.Should().Contain("good.ffpipe");
    }

    [Fact]
    public void Validate_removes_whitespace_only_entries()
    {
        var settings = new AppSettings
        {
            RecentPipelines = new List<string> { "   ", Path.Combine(Path.GetTempPath(), "real.ffpipe") }
        };
        settings.Validate();

        settings.RecentPipelines.Should().ContainSingle()
            .Which.Should().Contain("real.ffpipe");
    }
}
