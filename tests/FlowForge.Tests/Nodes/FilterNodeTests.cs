using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class FilterNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object conditions)
    {
        string json = JsonSerializer.Serialize(new { conditions });
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task File_matching_extension_passes()
    {
        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" }
        }));

        var job = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "photo.jpg"),
            CurrentPath = Path.Combine("/tmp", "photo.jpg")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().HaveCount(1);
        result.First().NodeLog.Should().Contain(l => l.Contains("passed"));
    }

    [Fact]
    public async Task File_not_matching_extension_is_dropped()
    {
        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" }
        }));

        var job = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "document.pdf"),
            CurrentPath = Path.Combine("/tmp", "document.pdf")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Contains_operator_matches_substring()
    {
        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "contains", value = "photo" }
        }));

        var job = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "my_photo_001.jpg"),
            CurrentPath = Path.Combine("/tmp", "my_photo_001.jpg")
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Matches_operator_uses_regex()
    {
        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "filename", @operator = "matches", value = @"^\d{3}\.jpg$" }
        }));

        var jobMatch = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "001.jpg"),
            CurrentPath = Path.Combine("/tmp", "001.jpg")
        };
        var jobNoMatch = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "abc.jpg"),
            CurrentPath = Path.Combine("/tmp", "abc.jpg")
        };

        IEnumerable<FileJob> resultMatch = await node.TransformAsync(jobMatch, dryRun: true);
        IEnumerable<FileJob> resultNoMatch = await node.TransformAsync(jobNoMatch, dryRun: true);

        resultMatch.Should().HaveCount(1);
        resultNoMatch.Should().BeEmpty();
    }

    [Fact]
    public async Task Size_filter_with_real_file()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "small.txt");
        File.WriteAllText(filePath, "hello");

        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "size", @operator = "lessThan", value = "1000" }
        }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task Multiple_conditions_all_must_match()
    {
        var node = new FilterNode();
        node.Configure(MakeConfig(new[]
        {
            new { field = "extension", @operator = "equals", value = ".jpg" },
            new { field = "filename", @operator = "startsWith", value = "photo" }
        }));

        var jobBoth = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "photo_001.jpg"),
            CurrentPath = Path.Combine("/tmp", "photo_001.jpg")
        };
        var jobExtOnly = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "document.jpg"),
            CurrentPath = Path.Combine("/tmp", "document.jpg")
        };

        IEnumerable<FileJob> resultBoth = await node.TransformAsync(jobBoth, dryRun: true);
        IEnumerable<FileJob> resultExtOnly = await node.TransformAsync(jobExtOnly, dryRun: true);

        resultBoth.Should().HaveCount(1);
        resultExtOnly.Should().BeEmpty();
    }

    [Fact]
    public void Missing_conditions_throws()
    {
        var node = new FilterNode();
        var config = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>();
    }
}
