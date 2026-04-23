using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Sources;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Nodes;

public class FolderInputNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static async Task<List<FileJob>> CollectJobsAsync(
        FolderInputNode node, CancellationToken ct = default)
    {
        var jobs = new List<FileJob>();
        await foreach (FileJob job in node.ProduceAsync(ct))
        {
            jobs.Add(job);
        }
        return jobs;
    }

    [Fact]
    public async Task Enumerates_all_files_in_folder()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("alpha.txt", "bravo.txt", "charlie.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(3);
        jobs.Select(j => Path.GetFileName(j.OriginalPath))
            .Should().BeEquivalentTo("alpha.txt", "bravo.txt", "charlie.txt");
    }

    [Fact]
    public async Task Recursive_true_finds_files_in_subdirectories()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("top.txt", "sub/nested.txt", "sub/deep/bottom.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path, recursive = true }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(3);
        jobs.Select(j => Path.GetFileName(j.OriginalPath))
            .Should().BeEquivalentTo("top.txt", "nested.txt", "bottom.txt");
    }

    [Fact]
    public async Task Recursive_false_returns_only_top_level_files()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("top.txt", "sub/nested.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path, recursive = false }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(1);
        Path.GetFileName(jobs[0].OriginalPath).Should().Be("top.txt");
    }

    [Fact]
    public async Task Filter_with_semicolon_separated_patterns_returns_only_matching()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg", "image.png", "readme.txt", "data.csv");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path, filter = "*.jpg;*.png" }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(2);
        jobs.Select(j => Path.GetFileName(j.OriginalPath))
            .Should().BeEquivalentTo("photo.jpg", "image.png");
    }

    [Fact]
    public async Task Empty_directory_yields_nothing()
    {
        using var dir = new TempDirectory();
        string emptyDir = Path.Combine(dir.Path, "empty");
        Directory.CreateDirectory(emptyDir);

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = emptyDir }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_existent_directory_throws_DirectoryNotFoundException()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), "FlowForge_NonExistent_" + Guid.NewGuid().ToString("N"));

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = fakePath }));

        Func<Task> act = async () => await CollectJobsAsync(node);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage($"*{fakePath}*");
    }

    [Fact]
    public void Missing_path_config_throws_NodeConfigurationException()
    {
        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        var config = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Empty_or_whitespace_path_throws_friendly_configuration_error(string emptyPath)
    {
        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        Dictionary<string, JsonElement> config = MakeConfig(new { path = emptyPath });

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*source folder*");
    }

    [Fact]
    public async Task Overlapping_patterns_do_not_produce_duplicates()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg", "other.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path, filter = "*.jpg;*.jpg" }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(1);
        Path.GetFileName(jobs[0].OriginalPath).Should().Be("photo.jpg");
    }

    [Fact]
    public async Task CancellationToken_stops_enumeration()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt", "d.txt", "e.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path }));

        using var cts = new CancellationTokenSource();
        var jobs = new List<FileJob>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (FileJob job in node.ProduceAsync(cts.Token))
            {
                jobs.Add(job);
                if (jobs.Count == 2)
                {
                    cts.Cancel();
                }
            }
        });

        jobs.Count.Should().BeLessThan(5, "cancellation should stop enumeration before all files are yielded");
    }

    [Fact]
    public async Task Files_returned_in_case_insensitive_sorted_order()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("Zebra.txt", "apple.txt", "Mango.txt", "banana.txt");

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = dir.Path }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        var fileNames = jobs.Select(j => Path.GetFileName(j.OriginalPath)).ToList();
        fileNames.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        fileNames.Should().HaveCount(4);
    }

    [Fact]
    public async Task Path_with_trailing_separator_still_enumerates_files()
    {
        // Reproduces the bug where Path.GetFullPath preserves a user-typed
        // trailing slash, making resolvedRootPrefix end in "\\\\" (doubled)
        // and every enumerated file being rejected as "outside source root".
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt");

        string pathWithTrailingSeparator = dir.Path + Path.DirectorySeparatorChar;

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = pathWithTrailingSeparator }));

        List<FileJob> jobs = await CollectJobsAsync(node);
        jobs.Should().HaveCount(3);
    }

    [Fact]
    public async Task Recursive_with_trailing_separator_enumerates_subdirectory_files()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("top.txt");
        Directory.CreateDirectory(Path.Combine(dir.Path, "sub"));
        File.WriteAllText(Path.Combine(dir.Path, "sub", "nested.txt"), string.Empty);

        string pathWithTrailingSeparator = dir.Path + Path.DirectorySeparatorChar;

        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(MakeConfig(new { path = pathWithTrailingSeparator, recursive = true }));

        List<FileJob> jobs = await CollectJobsAsync(node);
        jobs.Select(j => Path.GetFileName(j.OriginalPath))
            .Should().BeEquivalentTo(new[] { "top.txt", "nested.txt" });
    }
}
