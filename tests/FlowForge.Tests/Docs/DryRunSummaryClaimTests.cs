using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// The CLI's dry-run summary counts the files whose preview may still differ from the real run; the user guide
/// must name that line by the label the CLI prints.
/// </summary>
public class DryRunSummaryClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public DryRunSummaryClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guide_names_the_dry_run_summary_line()
    {
        var mismatches = new List<string>();
        string text = File.ReadAllText(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        if (!text.Contains($"`{Program.MarkedCountLabel}: n`", StringComparison.Ordinal))
        {
            mismatches.Add($"{GuidePath}:1: does not name the dry-run summary line `{Program.MarkedCountLabel}: n`");
        }

        ClaimCheck.Report(_output, "dry-run-summary", 1, 1, mismatches);
    }
}
