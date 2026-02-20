using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Sources;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Nodes;

public class FolderInputNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
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

        var node = new FolderInputNode();
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

        var node = new FolderInputNode();
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

        var node = new FolderInputNode();
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

        var node = new FolderInputNode();
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

        var node = new FolderInputNode();
        node.Configure(MakeConfig(new { path = emptyDir }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Non_existent_directory_throws_DirectoryNotFoundException()
    {
        string fakePath = Path.Combine(Path.GetTempPath(), "FlowForge_NonExistent_" + Guid.NewGuid().ToString("N"));

        var node = new FolderInputNode();
        node.Configure(MakeConfig(new { path = fakePath }));

        Func<Task> act = async () => await CollectJobsAsync(node);

        await act.Should().ThrowAsync<DirectoryNotFoundException>()
            .WithMessage($"*{fakePath}*");
    }

    [Fact]
    public void Missing_path_config_throws_NodeConfigurationException()
    {
        var node = new FolderInputNode();
        var config = new Dictionary<string, JsonElement>();

        Action act = () => node.Configure(config);

        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task Overlapping_patterns_do_not_produce_duplicates()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo.jpg", "other.txt");

        var node = new FolderInputNode();
        node.Configure(MakeConfig(new { path = dir.Path, filter = "*.jpg;*.jpg" }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        jobs.Should().HaveCount(1);
        Path.GetFileName(jobs[0].OriginalPath).Should().Be("photo.jpg");
    }

    [Fact]
    public async Task Files_returned_in_case_insensitive_sorted_order()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("Zebra.txt", "apple.txt", "Mango.txt", "banana.txt");

        var node = new FolderInputNode();
        node.Configure(MakeConfig(new { path = dir.Path }));

        List<FileJob> jobs = await CollectJobsAsync(node);

        List<string> fileNames = jobs.Select(j => Path.GetFileName(j.OriginalPath)).ToList();
        fileNames.Should().BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        fileNames.Should().HaveCount(4);
    }
}
