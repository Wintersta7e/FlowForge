using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

// A test count in a document goes stale with the next test added, so no document states one.
public partial class TestCountClaimTests
{
    private static readonly string[] DocumentPaths = ["README.md", "docs/USER_GUIDE.md", "samples/README.md"];

    private readonly ITestOutputHelper _output;

    public TestCountClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Documents_state_no_test_count()
    {
        var mismatches = new List<string>();
        foreach (string path in DocumentPaths)
        {
            string[] lines = File.ReadAllLines(RepoRoot.Combine(path.Split('/')));
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in TestCount().Matches(lines[i]))
                {
                    mismatches.Add($"{path}:{i + 1}: states a test count ('{match.Value}')");
                }
            }
        }

        ClaimCheck.Report(_output, "test-count", DocumentPaths.Length, 3, mismatches);
    }

    [GeneratedRegex(@"\b\d+ tests\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TestCount();
}
