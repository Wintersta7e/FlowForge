using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class RenameRegexNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object obj)
    {
        string json = JsonSerializer.Serialize(obj);
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
    public async Task Filename_regex_replacement_replaces_digits_with_X()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "photo123.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("photoX.jpg");
    }

    [Fact]
    public async Task Capture_groups_with_backreferences_in_replacement()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = @"(\w+)-(\w+)", replacement = "$2_$1" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "hello-world.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("world_hello.txt");
    }

    [Fact]
    public async Task Scope_filename_only_matches_filename_part()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = "tmp", replacement = "REPLACED", scope = "filename" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "tmp_file.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("REPLACED_file.txt");
        output.DirectoryName.Should().Be("/tmp");
    }

    [Fact]
    public async Task Scope_fullpath_matches_entire_path_within_directory()
    {
        using var tempDir = new TempDirectory();
        tempDir.CreateFiles("abc_test.txt");

        string filePath = Path.Combine(tempDir.Path, "abc_test.txt");
        FileJob job = MakeJob(filePath);

        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = "abc", replacement = "xyz", scope = "fullpath" }));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("xyz_test.txt");
        output.DirectoryName.Should().Be(tempDir.Path);
    }

    [Fact]
    public async Task Fullpath_path_traversal_throws_InvalidOperationException()
    {
        using var tempDir = new TempDirectory();
        tempDir.CreateFiles("safe.txt");

        string filePath = Path.Combine(tempDir.Path, "safe.txt");
        FileJob job = MakeJob(filePath);

        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = "safe\\.txt", replacement = "../escaped.txt", scope = "fullpath" }));

        Func<Task> act = () => node.TransformAsync(job, dryRun: true);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*escapes source directory*");
    }

    [Fact]
    public void Missing_pattern_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode();
        var config = MakeConfig(new { replacement = "X" });

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*pattern*required*");
    }

    [Fact]
    public void Invalid_regex_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode();

        Action act = () => node.Configure(MakeConfig(new { pattern = "[invalid", replacement = "X" }));
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*Invalid regex*");
    }

    [Fact]
    public void Missing_replacement_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode();
        var config = MakeConfig(new { pattern = @"\d+" });

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*replacement*required*");
    }

    [Fact]
    public async Task No_matches_leaves_filename_unchanged()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "nodigits.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("nodigits.txt");
    }

    [Fact]
    public async Task DryRun_updates_CurrentPath_without_calling_File_Move()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine("/nonexistent/path", "file99.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("fileX.txt");
        output.CurrentPath.Should().Be(Path.Combine("/nonexistent/path", "fileX.txt"));
        output.NodeLog.Should().ContainSingle()
            .Which.Should().Contain("RenameRegex:");
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        var node = new RenameRegexNode();
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine("/tmp", "file1.txt"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, dryRun: true, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
