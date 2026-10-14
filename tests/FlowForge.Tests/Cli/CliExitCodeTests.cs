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
    // stdout lines that must appear in this order. The tokens {missing}, {bad} and {valid}
    // in the arguments are replaced with fixture paths.
    public static TheoryData<string, string[], int, bool, string[]> Cases { get; } = new()
    {
        { "missing pipeline file", ["run", "{missing}"], 2, false, [] },
        { "invalid JSON", ["run", "{bad}"], 2, false, [] },
        { "missing argument", ["run"], 2, false, [] },
        { "invalid option value", ["run", "{valid}", "--format", "xml"], 2, true, [] },
        { "unknown option", ["run", "{valid}", "--bogus"], 2, true, [] },
        { "valid run", ["run", "{valid}"], 0, false, ["Scanning... 2 files found", "Processing 2 files..."] },
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

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{missing}"] = Path.Combine(dir.Path, "missing.ffpipe"),
            ["{bad}"] = bad,
            ["{valid}"] = valid,
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
}
