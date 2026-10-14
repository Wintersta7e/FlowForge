using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Cli;

public class CliExitCodeTests
{
    private readonly ITestOutputHelper _output;

    public CliExitCodeTests(ITestOutputHelper output) => _output = output;

    // Columns: name, arguments, expected exit code, whether the output folder must stay empty,
    // stdout lines that must appear in this order. The tokens {missing}, {bad}, {valid} and
    // {legacy} in the arguments are replaced with fixture paths.
    public static TheoryData<string, string[], int, bool, string[]> Cases { get; } = new()
    {
        { "missing pipeline file", ["run", "{missing}"], 2, false, [] },
        { "invalid JSON", ["run", "{bad}"], 2, false, [] },
        { "missing argument", ["run"], 2, false, [] },
        { "invalid option value", ["run", "{valid}", "--format", "xml"], 2, true, [] },
        { "unknown option", ["run", "{valid}", "--bogus"], 2, true, [] },
        { "valid run", ["run", "{valid}"], 0, false, ["Scanning... 2 files found", "Processing 2 files..."] },
        { "pipeline with load notices", ["run", "{legacy}"], 2, true, [] },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Exit_code_matches_the_documented_table(
        string name, string[] args, int expectedExit, bool outputMustStayEmpty, string[] stdoutInOrder)
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        string output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(input, "a.txt"), "a");
        await File.WriteAllTextAsync(Path.Combine(input, "b.txt"), "b");

        string bad = Path.Combine(dir.Path, "bad.ffpipe");
        await File.WriteAllTextAsync(bad, "{ not json");

        string valid = Path.Combine(dir.Path, "valid.ffpipe");
        PipelineGraph graph = PipelineBuilder.New("CLI exit code")
            .AddSource("FolderInput", new { path = input })
            .AddOutput("FolderOutput", new { path = output, mode = "copy" })
            .Build();
        await PipelineSerializer.SaveAsync(graph, valid, CancellationToken.None);

        // An image the legacy pipeline would resize and deliver, were it allowed to run.
        string legacyInput = Path.Combine(dir.Path, "legacy-in");
        Directory.CreateDirectory(legacyInput);
        TestFileFactory.CreateTestPng(Path.Combine(legacyInput, "photo.png"));
        string legacy = Path.Combine(dir.Path, "legacy.ffpipe");
        await File.WriteAllTextAsync(legacy, LegacyPipeline(legacyInput, output));

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{missing}"] = Path.Combine(dir.Path, "missing.ffpipe"),
            ["{bad}"] = bad,
            ["{valid}"] = valid,
            ["{legacy}"] = legacy,
        };
        string[] resolved = args.Select(a => tokens.TryGetValue(a, out string? path) ? path : a).ToArray();

        CliRun run = await CliProcess.RunAsync(resolved, dir.Path, CancellationToken.None);

        _output.WriteLine($"[cli-exit] {name}: exit={run.ExitCode} cases={Cases.Count}");
        string nl = Environment.NewLine;
        string detail = $"exit={run.ExitCode}{nl}stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}";
        run.ExitCode.Should().Be(expectedExit, "{0}", detail);

        if (outputMustStayEmpty)
        {
            Directory.EnumerateFileSystemEntries(output).Should().BeEmpty("{0}", detail);
        }

        int previous = -1;
        foreach (string line in stdoutInOrder)
        {
            int index = run.StdOut.IndexOf(line, StringComparison.Ordinal);
            index.Should().BeGreaterThan(previous, "'{0}' must follow the previous line in stdout{1}{2}", line, nl, detail);
            previous = index;
        }
    }

    [Fact]
    public async Task Pipeline_with_load_notices_runs_nothing_and_exits_2()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        string output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        TestFileFactory.CreateTestPng(Path.Combine(input, "photo.png"));
        await File.WriteAllTextAsync(Path.Combine(output, "existing.txt"), "kept");
        string legacy = Path.Combine(dir.Path, "legacy.ffpipe");
        await File.WriteAllTextAsync(legacy, LegacyPipeline(input, output));
        IReadOnlyList<CompatNotice> notices = (await PipelineSerializer.LoadAsync(legacy)).LoadNotices;
        var before = TreeSnapshot.Take(output);

        CliRun run = await CliProcess.RunAsync(["run", legacy], dir.Path, CancellationToken.None);

        string nl = Environment.NewLine;
        string detail = $"exit={run.ExitCode}{nl}stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}";
        string[] stderr = run.StdErr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        _output.WriteLine($"[cli-notices] notices={notices.Count} stderr-lines={stderr.Length}");
        notices.Should().NotBeEmpty();
        run.ExitCode.Should().Be(2, "{0}", detail);
        string[] expected = notices.Select(n => n.NodeId is null ? n.Message : $"{n.TypeKey} ({n.NodeId}): {n.Message}").ToArray();
        stderr.Should().HaveCount(expected.Length + 1, "each notice and then the re-save line{0}{1}", nl, detail);
        stderr.Take(expected.Length).Should().Equal(expected, "{0}", detail);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(output), _output, "output");
    }

    // The one hand-written pipeline file in this folder: it carries an older version, so it loads with notices.
    private static string LegacyPipeline(string input, string output) => $$"""
        {
          "name": "Legacy resize",
          "version": "1.0",
          "nodes": [
            { "id": "0d000000-0000-0000-0000-000000000001", "typeKey": "FolderInput", "config": { "path": {{JsonSerializer.Serialize(input)}} } },
            { "id": "0d000000-0000-0000-0000-000000000002", "typeKey": "ImageResize", "config": { "width": 640 } },
            { "id": "0d000000-0000-0000-0000-000000000003", "typeKey": "FolderOutput", "config": { "path": {{JsonSerializer.Serialize(output)}}, "mode": "copy" } }
          ],
          "connections": [
            { "fromNode": "0d000000-0000-0000-0000-000000000001", "toNode": "0d000000-0000-0000-0000-000000000002" },
            { "fromNode": "0d000000-0000-0000-0000-000000000002", "toNode": "0d000000-0000-0000-0000-000000000003" }
          ]
        }
        """;
}
