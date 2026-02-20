using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Nodes;

public class RenameAddAffixNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
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
    public async Task Prefix_only_prepends_to_filename()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { prefix = "IMG_" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("IMG_photo.jpg");
    }

    [Fact]
    public async Task Suffix_only_appends_before_extension()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { suffix = "_backup" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("photo_backup.jpg");
    }

    [Fact]
    public async Task Both_prefix_and_suffix_applied()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { prefix = "PRE_", suffix = "_SUF" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("PRE_photo_SUF.jpg");
    }

    [Fact]
    public void Both_empty_throws_NodeConfigurationException()
    {
        var node = new RenameAddAffixNode();

        Action act = () => node.Configure(MakeConfig(new { prefix = "", suffix = "" }));

        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task Extension_preserved_with_prefix_and_suffix()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { prefix = "A_", suffix = "_Z" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "document.tar.gz"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("A_document.tar_Z.gz");
        Path.GetExtension(output.CurrentPath).Should().Be(".gz");
    }

    [Fact]
    public async Task DryRun_updates_path_without_file_move()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { prefix = "NEW_" }));

        FileJob job = MakeJob(Path.Combine("/nonexistent/path", "test.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("NEW_test.jpg");
        output.CurrentPath.Should().Be(Path.Combine("/nonexistent/path", "NEW_test.jpg"));
        output.NodeLog.Should().ContainSingle()
            .Which.Should().Contain("RenameAddAffix:");
    }

    [Fact]
    public async Task CancellationToken_respected()
    {
        var node = new RenameAddAffixNode();
        node.Configure(MakeConfig(new { prefix = "X_" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "file.txt"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, dryRun: true, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
