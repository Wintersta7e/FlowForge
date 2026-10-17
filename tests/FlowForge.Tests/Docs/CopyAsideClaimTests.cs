using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// When a run delivers a file onto another of its inputs, that input is copied aside before the first delivery, and
/// a dry run says so in the input's node log. The user guide must quote that line as the runner writes it.
/// </summary>
public class CopyAsideClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public CopyAsideClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Guide_quotes_the_dry_run_copy_aside_line()
    {
        using var dir = new TempDirectory();
        string data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        string replaced = Path.Combine(data, "002.txt");
        await File.WriteAllTextAsync(replaced, "TWO");
        await File.WriteAllTextAsync(Path.Combine(data, "a.txt"), "AAA");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = data })
            .AddTransform("Sort", new { field = "filename", direction = "asc" })
            .AddTransform("RenamePattern", new { pattern = "{counter:000}{ext}" })
            .AddOutput("FolderOutput", new { path = data, overwrite = true })
            .Build();

        ExecutionResult result = await new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot)
            .RunAsync(graph, dryRun: true);

        // a.txt is renumbered onto 002.txt, so the line sits on the job that reads 002.txt and quotes its path.
        var mismatches = new List<string>();
        string quoted = $"'{replaced}'";
        FileJob reader = result.Jobs.Single(j => string.Equals(j.OriginalPath, replaced, StringComparison.Ordinal));
        string[] lines = reader.NodeLog.Where(line => line.Contains(quoted, StringComparison.Ordinal)).ToArray();
        if (lines.Length != 1)
        {
            mismatches.Add($"dry run: {lines.Length} node-log lines quote {quoted}, expected the one copy-aside line: {string.Join(" | ", reader.NodeLog)}");
            ClaimCheck.Report(_output, "copy-aside", 0, 1, mismatches);
            return;
        }

        string line = lines[0].Replace(quoted, "'…'", StringComparison.Ordinal);
        string text = File.ReadAllText(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        if (!text.Contains($"`{line}`", StringComparison.Ordinal))
        {
            mismatches.Add($"{GuidePath}:1: does not quote the dry run's copy-aside line `{line}`");
        }

        ClaimCheck.Report(_output, "copy-aside", 1, 1, mismatches);
    }
}
