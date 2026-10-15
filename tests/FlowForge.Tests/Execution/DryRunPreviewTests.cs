using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public partial class DryRunPreviewTests
{
    private readonly ITestOutputHelper _output;

    public DryRunPreviewTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(PreviewPipelineRows.PreviewRows), MemberType = typeof(PreviewPipelineRows))]
    public async Task Preview_destinations_equal_live_destinations(string row)
    {
        using var dir = new TempDirectory();
        string root = Path.Combine(dir.Path, "root");
        PreviewPipelineRows.CreateFixture(Path.Combine(root, "input"));
        await PrepopulateOutputAsync(row, dir, root);

        var before = TreeSnapshot.Take(root);
        ExecutionResult dry = await CreateRunner(dir.StagingRoot).RunAsync(await LoadReplacingAsync(row, root), dryRun: true);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(root), _output, row);
        ExecutionResult live = await CreateRunner(dir.StagingRoot).RunAsync(await LoadReplacingAsync(row, root));

        // Every row has one output, so the original path alone identifies a job's delivery.
        var liveByOriginal = live.Jobs.ToDictionary(j => j.OriginalPath, StringComparer.Ordinal);
        var previewByOriginal = dry.Jobs
            .Where(j => PreviewPath(j) is not null)
            .ToDictionary(j => j.OriginalPath, j => PreviewPath(j)!, StringComparer.Ordinal);

        // A job the live run did not deliver claimed nothing, so later names in its folder may shift.
        var skippedFolders = new HashSet<string>(
            previewByOriginal
                .Where(p => !liveByOriginal.TryGetValue(p.Key, out FileJob? j) || j.Status != FileJobStatus.Succeeded)
                .Select(p => Path.GetDirectoryName(p.Value)!),
            StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<string>();
        int compared = 0;
        foreach (FileJob job in dry.Jobs.Where(j => j.PreviewMarker != PreviewMarker.Unresolved))
        {
            liveByOriginal.TryGetValue(job.OriginalPath, out FileJob? liveJob);
            string? preview = PreviewPath(job);
            string? delivered = liveJob?.Status == FileJobStatus.Succeeded ? liveJob.CurrentPath : null;
            string? folder = Path.GetDirectoryName(preview ?? delivered);
            if (folder is null || skippedFolders.Contains(folder))
            {
                continue;
            }

            compared++;
            if (!string.Equals(preview, delivered, StringComparison.Ordinal))
            {
                mismatches.Add($"{Path.GetFileName(job.OriginalPath)}: preview '{preview}' live '{delivered}'");
            }
        }

        int unresolved = dry.Jobs.Count(j => j.PreviewMarker == PreviewMarker.Unresolved);
        int checkedAtRun = dry.Jobs.Count(j => j.PreviewMarker == PreviewMarker.CheckedAtRun);
        _output.WriteLine($"[preview] {row} compared={compared} unresolved={unresolved} checked_at_run={checkedAtRun}");
        mismatches.ForEach(_output.WriteLine);
        mismatches.Should().BeEmpty();

        // A row whose every preview depends on file content compares nothing, but must still mark each job.
        (compared + unresolved).Should().BePositive("the row must preview at least one job");
    }

    [Fact]
    public async Task Size_filter_and_sort_match_live()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        foreach ((string name, int size) in new[] { ("a.bin", 3000), ("b.bin", 1500), ("c.bin", 200), ("d.bin", 5000), ("e.bin", 1000), ("f.bin", 1001) })
        {
            await File.WriteAllBytesAsync(Path.Combine(input, name), new byte[size]);
        }

        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input })
            .AddTransform("Filter", new { conditions = new[] { new { field = "size", @operator = "greaterthan", value = "1000" } } })
            .AddTransform("Sort", new { field = "size", direction = "asc" })
            .AddTransform("RenamePattern", new { pattern = "{counter:00}_{name}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath })
            .Build();

        ExecutionResult dry = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: true);
        ExecutionResult live = await CreateRunner(dir.StagingRoot).RunAsync(graph);

        string[] previewed = dry.Jobs.Select(PreviewPath).OfType<string>().Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();
        string[] delivered = live.Jobs.Where(j => j.Status == FileJobStatus.Succeeded)
            .Select(j => Path.GetFileName(j.CurrentPath)).Order(StringComparer.Ordinal).ToArray();
        _output.WriteLine($"[preview] dry={string.Join(',', previewed)} live={string.Join(',', delivered)}");
        delivered.Should().Equal("01_f.bin", "02_b.bin", "03_a.bin", "04_d.bin");
        previewed.Should().Equal(delivered);
    }

    [Fact]
    public async Task Missing_metadata_key_is_empty_live_and_unresolved_in_preview()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        TestFileFactory.CreateTestImage(Path.Combine(input, "sub1", "a.jpg"));
        TestFileFactory.CreateTestImage(Path.Combine(input, "sub2", "a.jpg"));
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, recursive = true })
            .AddTransform("MetadataExtract", new { keys = new[] { "EXIF:DateTaken" } })
            .AddTransform("RenamePattern", new { pattern = "{meta:EXIF:DateTaken}_{name}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath })
            .Build();

        ExecutionResult dry = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: true);
        ExecutionResult live = await CreateRunner(dir.StagingRoot).RunAsync(graph);

        int unresolved = dry.Jobs.Count(j => j.PreviewMarker == PreviewMarker.Unresolved);
        _output.WriteLine($"[preview] unresolved={unresolved}");
        live.Jobs.Where(j => j.Status == FileJobStatus.Succeeded).Select(j => Path.GetFileName(j.CurrentPath))
            .Should().BeEquivalentTo("_a.jpg", "_a_1.jpg");
        unresolved.Should().Be(2);
    }

    /// <summary>The destination FolderOutput logged for the job in a dry run, or null when it reached no output.</summary>
    private static string? PreviewPath(FileJob job) =>
        job.NodeLog.Select(line => DryRunDestination().Match(line)).Where(m => m.Success).Select(m => m.Groups["path"].Value).LastOrDefault();

    /// <summary>Fills the output folder with every name a live run produces, then loads the row to replace them with backups.</summary>
    private static async Task PrepopulateOutputAsync(string row, TempDirectory dir, string root)
    {
        string scratch = Path.Combine(dir.Path, "scratch");
        PreviewPipelineRows.CreateFixture(Path.Combine(scratch, "input"));
        await CreateRunner(dir.StagingRoot).RunAsync(await PreviewPipelineRows.LoadAsync(row, scratch));

        string scratchOutput = Path.Combine(scratch, "output");
        string[] produced = Directory.Exists(scratchOutput)
            ? Directory.GetFiles(scratchOutput, "*", SearchOption.AllDirectories)
            : [];
        produced.Should().NotBeEmpty("a live run must produce the names the preview meets");
        foreach (string file in produced)
        {
            string target = Path.Combine(root, "output", Path.GetRelativePath(scratchOutput, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task<PipelineGraph> LoadReplacingAsync(string row, string root)
    {
        PipelineGraph graph = await PreviewPipelineRows.LoadAsync(row, root);
        foreach (NodeDefinition node in graph.Nodes.Where(n => string.Equals(n.TypeKey, "FolderOutput", StringComparison.Ordinal)))
        {
            node.Config["overwrite"] = JsonSerializer.SerializeToElement(true);
            node.Config["enableBackup"] = JsonSerializer.SerializeToElement(true);
        }

        return graph;
    }

    private static PipelineRunner CreateRunner(string stagingRoot) =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: stagingRoot);

    [GeneratedRegex(@"^FolderOutput: → '(?<path>.+)' \([^)]*\) \[dry-run\]$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DryRunDestination();
}
