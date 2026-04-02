using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Nodes;

public class RenameRegexNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object obj)
    {
        string json = JsonSerializer.Serialize(obj);
        var doc = JsonDocument.Parse(json);
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
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "photo123.jpg"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("photoX.jpg");
    }

    [Fact]
    public async Task Capture_groups_with_backreferences_in_replacement()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = @"(\w+)-(\w+)", replacement = "$2_$1" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "hello-world.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("world_hello.txt");
    }

    [Fact]
    public async Task Scope_filename_only_matches_filename_part()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = "tmp", replacement = "REPLACED", scope = "filename" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "tmp_file.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("REPLACED_file.txt");
        output.DirectoryName.Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public async Task Scope_fullpath_matches_entire_path_within_directory()
    {
        using var tempDir = new TempDirectory();
        tempDir.CreateFiles("abc_test.txt");

        string filePath = Path.Combine(tempDir.Path, "abc_test.txt");
        FileJob job = MakeJob(filePath);

        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = "abc", replacement = "xyz", scope = "fullpath" }));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("xyz_test.txt");
        output.DirectoryName.Should().Be(tempDir.Path);
    }

    [Fact]
    public async Task Fullpath_path_traversal_sets_failed_status()
    {
        using var tempDir = new TempDirectory();
        tempDir.CreateFiles("safe.txt");

        string filePath = Path.Combine(tempDir.Path, "safe.txt");
        FileJob job = MakeJob(filePath);

        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = "safe\\.txt", replacement = "../escaped.txt", scope = "fullpath" }));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("path traversal blocked");
    }

    [Fact]
    public void Missing_pattern_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        Dictionary<string, JsonElement> config = MakeConfig(new { replacement = "X" });

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*pattern*required*");
    }

    [Fact]
    public void Invalid_regex_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);

        Action act = () => node.Configure(MakeConfig(new { pattern = "[invalid", replacement = "X" }));
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*Invalid regex*");
    }

    [Fact]
    public void Missing_replacement_throws_NodeConfigurationException()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        Dictionary<string, JsonElement> config = MakeConfig(new { pattern = @"\d+" });

        Action act = () => node.Configure(config);
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*replacement*required*");
    }

    [Fact]
    public async Task No_matches_leaves_filename_unchanged()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "nodigits.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        FileJob output = result.Single();
        output.FileName.Should().Be("nodigits.txt");
    }

    [Fact]
    public async Task DryRun_updates_CurrentPath_without_calling_File_Move()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
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
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = @"\d+", replacement = "X" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "file1.txt"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, dryRun: true, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TransformAsync_pathological_regex_times_out_and_fails()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = @"(a+)+$", replacement = "x", scope = "filename" }));

        // Pathological input: many 'a's followed by '!' to trigger catastrophic backtracking
        string maliciousName = new string('a', 30) + "!.txt";
        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), maliciousName));

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("regex match timed out");
    }

    [Fact]
    public async Task Fullpath_scope_happy_path_with_real_file()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "old_name.txt");
        File.WriteAllText(filePath, "test");

        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = "old_name", replacement = "new_name", scope = "fullpath" }));

        FileJob job = MakeJob(filePath);
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);

        result.Should().ContainSingle();
        string expectedPath = Path.Combine(dir.Path, "new_name.txt");
        result.First().CurrentPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task Filename_scope_rejects_path_separator_in_replacement()
    {
        var node = new RenameRegexNode(NullLogger<RenameRegexNode>.Instance);
        node.Configure(MakeConfig(new { pattern = "file", replacement = "sub/file", scope = "filename" }));

        FileJob job = MakeJob(Path.Combine(Path.GetTempPath(), "file.txt"));
        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);

        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("path-like filename");
    }
}
