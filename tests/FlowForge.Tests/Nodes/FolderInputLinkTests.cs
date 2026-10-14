using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Sources;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Nodes;

public class FolderInputLinkTests
{
    private readonly ITestOutputHelper _output;

    public FolderInputLinkTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Recursive_input_skips_file_and_directory_links()
    {
        using var dir = new TempDirectory();
        (string root, string outside) = CreateRootBesideOutside(dir);
        File.WriteAllText(Path.Combine(root, "inside.txt"), "inside");
        string hidden = Path.Combine(root, ".hidden.txt");
        File.WriteAllText(hidden, "hidden");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }

        LinkFactory.CreateFileSymlink(Path.Combine(root, "filelink.txt"), Path.Combine(outside, "victim.txt"));
        LinkFactory.CreateDirectorySymlink(Path.Combine(root, "dirlink"), outside);

        List<string> yielded = await DiscoverAsync(root);

        yielded.Should().NotBeEmpty();
        yielded.Should().BeEquivalentTo("inside.txt", ".hidden.txt");
        foreach (string relative in yielded)
        {
            string full = Path.GetFullPath(Path.Combine(root, relative));
            full.Should().StartWith(root + Path.DirectorySeparatorChar);
            new FileInfo(full).LinkTarget.Should().BeNull(relative);
        }
    }

    [Fact]
    public async Task Linked_directory_to_outside_is_not_descended()
    {
        using var dir = new TempDirectory();
        (string root, string outside) = CreateRootBesideOutside(dir);
        Directory.CreateDirectory(Path.Combine(root, "real"));
        File.WriteAllText(Path.Combine(root, "real", "kept.txt"), "kept");
        Directory.CreateDirectory(Path.Combine(outside, "deep"));
        File.WriteAllText(Path.Combine(outside, "deep", "more.txt"), "more");
        await LinkFactory.CreateDirectoryLinkAsync(Path.Combine(root, "linked"), outside);

        List<string> yielded = await DiscoverAsync(root);

        yielded.Should().Contain(Path.Combine("real", "kept.txt"));
        yielded.Should().NotContain(p => p.StartsWith("linked" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static (string Root, string Outside) CreateRootBesideOutside(TempDirectory dir)
    {
        string root = Path.Combine(dir.Path, "root");
        string outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "victim.txt"), "victim");
        return (root, outside);
    }

    private async Task<List<string>> DiscoverAsync(string root)
    {
        var node = new FolderInputNode(NullLogger<FolderInputNode>.Instance);
        node.Configure(new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement(root),
            ["recursive"] = JsonSerializer.SerializeToElement(true),
            ["filter"] = JsonSerializer.SerializeToElement("*"),
        });

        List<string> yielded = [];
        await foreach (FileJob job in node.ProduceAsync())
        {
            string relative = Path.GetRelativePath(root, job.OriginalPath);
            _output.WriteLine(relative);
            yielded.Add(relative);
        }

        _output.WriteLine($"yielded={yielded.Count}");
        return yielded;
    }
}
