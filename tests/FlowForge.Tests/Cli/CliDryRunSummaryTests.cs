using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Cli;

public class CliDryRunSummaryTests
{
    private readonly ITestOutputHelper _output;

    public CliDryRunSummaryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Dry_run_summary_prints_the_marked_job_count()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        TestFileFactory.CreateTestImage(Path.Combine(input, "one.jpg"));
        TestFileFactory.CreateTestImage(Path.Combine(input, "two.jpg"));
        string pipeline = Path.Combine(dir.Path, "web-export.ffpipe");
        PipelineGraph graph = PipelineBuilder.New("Web export")
            .AddSource("FolderInput", new { path = input, filter = "*.jpg;*.png" })
            .AddTransform("Filter", new { conditions = new[] { new { field = "extension", @operator = "equals", value = ".jpg" } } })
            .AddTransform("ImageResize", new { width = 1920, mode = "max" })
            .AddTransform("ImageConvert", new { format = "webp" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        await PipelineSerializer.SaveAsync(graph, pipeline, CancellationToken.None);

        CliRun run = await CliProcess.RunAsync(["run", pipeline, "--dry-run"], dir.Path, CancellationToken.None);

        string[] lines = (run.StdOut + Environment.NewLine + run.StdErr).Split(Environment.NewLine);
        string? marked = lines.FirstOrDefault(l => l.Contains(Program.MarkedCountLabel, StringComparison.Ordinal));
        _output.WriteLine($"[cli] exit={run.ExitCode} marked='{marked}'");
        run.ExitCode.Should().Be(0, run.StdErr);
        marked.Should().NotBeNull("stdout:{0}{1}", Environment.NewLine, run.StdOut);
        marked.Should().EndWith(": 2");
    }
}
