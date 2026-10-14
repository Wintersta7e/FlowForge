using FlowForge.Tests.Helpers;
using FluentAssertions;

namespace FlowForge.Tests.Cli;

public class CliVersionTests
{
    [Fact]
    public async Task Help_usage_names_the_flowforge_executable()
    {
        CliRun run = await CliProcess.RunAsync(["--help"], AppContext.BaseDirectory, CancellationToken.None);

        string nl = Environment.NewLine;
        run.ExitCode.Should().Be(0, "{0}", $"stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}");
        string[] lines = run.StdOut.ReplaceLineEndings("\n").Split('\n');
        int usage = Array.FindIndex(lines, l => string.Equals(l.Trim(), "Usage:", StringComparison.Ordinal));
        string usageLine = usage >= 0 ? lines.ElementAtOrDefault(usage + 1) ?? string.Empty : string.Empty;
        string program = usageLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        program.Should().Be("flowforge", "{0}", run.StdOut);
    }
}
