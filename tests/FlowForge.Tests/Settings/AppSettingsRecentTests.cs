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
}
