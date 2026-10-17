using System.Diagnostics;
using FlowForge.Core.Execution;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class DestinationRegistryTests
{
    private readonly ITestOutputHelper _output;

    public DestinationRegistryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Moved_original_that_a_claim_names_is_never_deleted()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string alias = Path.Combine(dir.Path, "alias");
        await LinkFactory.CreateDirectoryLinkAsync(alias, input);
        string replaced = Path.Combine(input, "replaced.txt");
        string backedUp = Path.Combine(input, "replaced.txt.bak");
        string aliased = Path.Combine(input, "aliased.txt");
        string free = Path.Combine(input, "free.txt");
        string[] originals = [replaced, backedUp, aliased, free];
        foreach (string original in originals)
        {
            await File.WriteAllTextAsync(original, Path.GetFileName(original));
        }

        var registry = new DestinationRegistry();
        foreach (string original in originals)
        {
            registry.RegisterOriginal(original);
            registry.RecordDelivery(original, byMove: true);
        }

        // Other jobs' claims: one replaces an original and backs it up over a second, one names a third through a linked folder.
        registry.Claim(input, replaced, overwrite: true, backupSuffix: ".bak").Kind.Should().Be(ClaimKind.ReplaceWithBackup);
        registry.Claim(alias, Path.Combine(alias, "aliased.txt"), overwrite: true, backupSuffix: null).Kind.Should().Be(ClaimKind.Replace);

        var deleted = originals.SelectMany(original => registry.CompleteBranch(original, failed: false)).ToList();

        _output.WriteLine($"[registry] deleted={string.Join(", ", deleted.Select(Path.GetFileName))}");
        deleted.Should().Equal(free);
    }

    [Fact]
    public void Moved_original_whose_links_cannot_be_resolved_is_kept()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string loop = Path.Combine(input, "loop.txt");
        LinkFactory.CreateFileSymlink(loop, loop);
        var registry = new DestinationRegistry();
        registry.RegisterOriginal(loop);
        registry.RecordDelivery(loop, byMove: true);
        registry.Claim(dir.OutputPath, Path.Combine(dir.OutputPath, "other.txt"), overwrite: false, backupSuffix: null);

        IReadOnlyList<string> deleted = registry.CompleteBranch(loop, failed: false);

        _output.WriteLine($"[registry] deleted={deleted.Count}");
        deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Ten_thousand_in_place_claims_and_completions_take_seconds()
    {
        const int count = 10_000;
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string[] originals = Enumerable.Range(0, count).Select(i => Path.Combine(input, $"f{i:00000}.txt")).ToArray();
        foreach (string original in originals)
        {
            await File.WriteAllTextAsync(original, "x");
        }

        var registry = new DestinationRegistry();
        var stopwatch = Stopwatch.StartNew();
        foreach (string original in originals)
        {
            registry.RegisterOriginal(original);
        }

        foreach (string original in originals)
        {
            registry.Claim(input, original, overwrite: true, backupSuffix: null).Kind.Should().Be(ClaimKind.Replace);
            registry.RecordDelivery(original, byMove: true);
        }

        int deleted = originals.Sum(original => registry.CompleteBranch(original, failed: false).Count);
        stopwatch.Stop();

        _output.WriteLine($"[registry] originals={count} deleted={deleted} seconds={stopwatch.Elapsed.TotalSeconds:F2}");
        deleted.Should().Be(0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    private static string CreateFolder(TempDirectory dir, string name)
    {
        string path = Path.Combine(dir.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
