using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class TemplatesClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";
    private const string StartMarker = "<!-- templates -->";
    private const string EndMarker = "<!-- /templates -->";

    private readonly ITestOutputHelper _output;

    public TemplatesClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void User_guide_template_table_matches_the_template_library()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        var expected = new List<string> { "| Template | Nodes |", "|---|---|" };
        foreach (PipelineTemplate template in PipelineTemplateLibrary.Templates)
        {
            IEnumerable<string> chain = ClaimCheck.TopologicalTypeKeys(template.Graph).Select(registry.GetDisplayName);
            expected.Add($"| {template.Name} | {string.Join(" → ", chain)} |");
        }

        string paste = string.Join(Environment.NewLine, [StartMarker, .. expected, EndMarker]);
        string[] lines = File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        var mismatches = new List<string>();
        int scanned = 0;

        (int StartLine, IReadOnlyList<string> Body)? block = ClaimCheck.Section(lines, StartMarker, EndMarker);
        if (block is null)
        {
            int templatesLine = Array.FindIndex(lines, l => string.Equals(l.Trim(), "## Templates", StringComparison.Ordinal)) + 1;
            mismatches.Add($"{GuidePath}:{Math.Max(templatesLine, 1)}: no template table; paste:{Environment.NewLine}{paste}");
        }
        else
        {
            IReadOnlyList<string> body = block.Value.Body;
            scanned = Math.Max(body.Count - 2, 0);
            for (int i = 0; i < Math.Max(body.Count, expected.Count); i++)
            {
                if (i >= body.Count || i >= expected.Count || !string.Equals(body[i], expected[i], StringComparison.Ordinal))
                {
                    mismatches.Add($"{GuidePath}:{block.Value.StartLine + 1 + i}: the template table differs from the template library; paste:{Environment.NewLine}{paste}");
                    break;
                }
            }
        }

        ClaimCheck.Report(_output, "templates", scanned, PipelineTemplateLibrary.Templates.Count, mismatches);
    }
}
