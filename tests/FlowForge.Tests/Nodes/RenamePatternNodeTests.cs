using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Nodes;

public class RenamePatternNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(string pattern, int startIndex = 1)
    {
        string json = JsonSerializer.Serialize(new { pattern, startIndex });
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static FileJob MakeJob(string filePath)
    {
        return new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };
    }

    [Fact]
    public async Task Name_token_replaces_with_filename_without_extension()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{name}_copy{ext}"));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("photo_copy.jpg");
    }

    [Fact]
    public async Task Ext_token_preserves_original_extension()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("renamed{ext}"));

        FileJob job = MakeJob(Path.Combine("/tmp", "document.PDF"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("renamed.PDF");
    }

    [Fact]
    public async Task Counter_zero_pads_correctly()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{counter:000}{ext}"));

        FileJob job1 = MakeJob(Path.Combine("/tmp", "a.jpg"));
        FileJob job2 = MakeJob(Path.Combine("/tmp", "b.jpg"));
        FileJob job3 = MakeJob(Path.Combine("/tmp", "c.jpg"));

        IEnumerable<FileJob> result1 = await node.TransformAsync(job1, dryRun: true);
        IEnumerable<FileJob> result2 = await node.TransformAsync(job2, dryRun: true);
        IEnumerable<FileJob> result3 = await node.TransformAsync(job3, dryRun: true);

        result1.Single().FileName.Should().Be("001.jpg");
        result2.Single().FileName.Should().Be("002.jpg");
        result3.Single().FileName.Should().Be("003.jpg");
    }

    [Fact]
    public async Task Counter_with_custom_start_index()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{counter:000}{ext}", startIndex: 10));

        FileJob job = MakeJob(Path.Combine("/tmp", "file.png"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        result.Single().FileName.Should().Be("010.png");
    }

    [Fact]
    public async Task Date_token_uses_today()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{date}_{name}{ext}"));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        string expected = $"{DateTime.Today:yyyy-MM-dd}_photo.jpg";
        result.Single().FileName.Should().Be(expected);
    }

    [Fact]
    public async Task Meta_token_resolves_from_metadata()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{meta:Author}_{name}{ext}"));

        var job = new FileJob
        {
            OriginalPath = Path.Combine("/tmp", "doc.txt"),
            CurrentPath = Path.Combine("/tmp", "doc.txt"),
            Metadata = { ["Author"] = "Alice" }
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Single().FileName.Should().Be("Alice_doc.txt");
    }

    [Fact]
    public async Task Meta_token_returns_empty_when_key_missing()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{meta:Missing}_{name}{ext}"));

        FileJob job = MakeJob(Path.Combine("/tmp", "doc.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        result.Single().FileName.Should().Be("_doc.txt");
    }

    [Fact]
    public async Task DryRun_does_not_call_file_move()
    {
        var node = new RenamePatternNode();
        node.Configure(MakeConfig("{name}_renamed{ext}"));

        // Use a path that doesn't exist — if File.Move were called, it would throw
        FileJob job = MakeJob(Path.Combine("/nonexistent/path", "test.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("test_renamed.jpg");
        output.NodeLog.Should().ContainSingle()
            .Which.Should().Contain("RenamePattern:");
    }

    [Fact]
    public void Missing_pattern_throws_NodeConfigurationException()
    {
        var node = new RenamePatternNode();
        var emptyConfig = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(emptyConfig);
        act.Should().Throw<NodeConfigurationException>();
    }
}
