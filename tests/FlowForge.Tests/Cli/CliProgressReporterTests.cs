using FlowForge.CLI;
using FlowForge.Core.Execution;
using FluentAssertions;

namespace FlowForge.Tests.Cli;

public class CliProgressReporterTests
{
    [Fact]
    public void Discovered_count_is_written_before_processing_starts()
    {
        using var status = new StringWriter();
        using var error = new StringWriter();
        var reporter = new CliProgressReporter(status, error, verbose: false);

        reporter.Report(new PhaseChanged(ExecutionPhase.Enumerating));
        reporter.Report(new FilesDiscovered(2));
        reporter.Report(new PhaseChanged(ExecutionPhase.Processing));

        string text = status.ToString();
        int scanned = text.IndexOf("Scanning... 2 files found", StringComparison.Ordinal);
        int processing = text.IndexOf("Processing 2 files...", StringComparison.Ordinal);
        scanned.Should().BeGreaterThanOrEqualTo(0, "the scan count must be written; status was '{0}'", text);
        processing.Should().BeGreaterThan(scanned, "processing must follow the scan count; status was '{0}'", text);
    }
}
