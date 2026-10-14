using System.Xml.Linq;
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

    [Fact]
    public async Task Version_output_starts_with_the_build_version()
    {
        string version = XDocument.Load(RepoRoot.Combine("Directory.Build.props")).Descendants("Version").Single().Value;

        CliRun run = await CliProcess.RunAsync(["--version"], AppContext.BaseDirectory, CancellationToken.None);

        // The SDK may append "+<commit>" to the informational version, so only the prefix is fixed.
        string nl = Environment.NewLine;
        run.ExitCode.Should().Be(0, "{0}", $"stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}");
        run.StdOut.Trim().Should().StartWith(version);
    }
}
