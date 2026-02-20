using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class MetadataExtractNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task File_SizeBytes_extracts_numeric_string()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "testfile.txt");
        File.WriteAllText(filePath, "some content here");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "File:SizeBytes" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().ContainKey("File:SizeBytes");
        resultJob.Metadata["File:SizeBytes"].Should().NotBeNullOrWhiteSpace();
        long.TryParse(resultJob.Metadata["File:SizeBytes"], out long size).Should().BeTrue();
        size.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task File_CreatedAt_extracts_formatted_date()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "testfile.txt");
        File.WriteAllText(filePath, "content");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "File:CreatedAt" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().ContainKey("File:CreatedAt");
        string createdAt = resultJob.Metadata["File:CreatedAt"];
        createdAt.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$");
    }

    [Fact]
    public async Task File_ModifiedAt_extracts_formatted_date()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "testfile.txt");
        File.WriteAllText(filePath, "content");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "File:ModifiedAt" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().ContainKey("File:ModifiedAt");
        string modifiedAt = resultJob.Metadata["File:ModifiedAt"];
        modifiedAt.Should().MatchRegex(@"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$");
    }

    [Fact]
    public async Task EXIF_key_on_non_EXIF_file_returns_null_and_logs_warning()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "plaintext.txt");
        File.WriteAllText(filePath, "this is not an image");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "EXIF:DateTaken" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().NotContainKey("EXIF:DateTaken");
        resultJob.NodeLog.Should().Contain(l => l.Contains("WARNING") && l.Contains("EXIF:DateTaken"));
    }

    [Fact]
    public async Task Missing_key_prefix_returns_null_and_logs_warning()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "testfile.txt");
        File.WriteAllText(filePath, "content");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "UnknownPrefix:Something" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().NotContainKey("UnknownPrefix:Something");
        resultJob.NodeLog.Should().Contain(l => l.Contains("WARNING") && l.Contains("UnknownPrefix:Something"));
    }

    [Fact]
    public void Empty_keys_array_throws_NodeConfigurationException()
    {
        var node = new MetadataExtractNode();
        Dictionary<string, JsonElement> config = MakeConfig(new { keys = Array.Empty<string>() });

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Keys_not_array_throws_NodeConfigurationException()
    {
        var node = new MetadataExtractNode();

        string json = JsonSerializer.Serialize(new { keys = "File:SizeBytes" });
        JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> config = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task Non_existent_file_returns_null_for_each_key_with_warnings()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), "does_not_exist_" + Guid.NewGuid().ToString("N") + ".txt");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "File:SizeBytes", "EXIF:DateTaken" } }));

        var job = new FileJob
        {
            OriginalPath = fakePath,
            CurrentPath = fakePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().BeEmpty();
        resultJob.NodeLog.Should().Contain(l => l.Contains("WARNING") && l.Contains("File:SizeBytes"));
        resultJob.NodeLog.Should().Contain(l => l.Contains("WARNING") && l.Contains("EXIF:DateTaken"));
    }

    [Fact]
    public async Task Multiple_keys_populate_metadata_correctly()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "multikey.txt");
        File.WriteAllText(filePath, "test data for multiple keys");

        var node = new MetadataExtractNode();
        node.Configure(MakeConfig(new { keys = new[] { "File:SizeBytes", "File:CreatedAt", "File:ModifiedAt" } }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().HaveCount(1);
        FileJob resultJob = result.First();
        resultJob.Metadata.Should().HaveCount(3);
        resultJob.Metadata.Should().ContainKey("File:SizeBytes");
        resultJob.Metadata.Should().ContainKey("File:CreatedAt");
        resultJob.Metadata.Should().ContainKey("File:ModifiedAt");
        resultJob.NodeLog.Should().Contain(l => l.Contains("extracted 3 keys"));
    }
}
