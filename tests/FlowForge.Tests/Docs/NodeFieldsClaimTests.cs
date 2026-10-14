using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class NodeFieldsClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";
    private const string BlockPrefix = "<!-- node-fields:";
    private const string EndMarker = "<!-- /node-fields -->";
    private const string None = "—";

    private static readonly string[] MarkdownSpecials = ["\\", "`", "*", "_", "[", "]", "<", ">", "|"];

    private readonly ITestOutputHelper _output;

    public NodeFieldsClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void User_guide_field_tables_match_each_node_schema()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        string[] typeKeys = registry.GetRegisteredTypeKeys().ToArray();
        string[] lines = File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        int nodesLine = Array.FindIndex(lines, l => string.Equals(l.Trim(), "## Nodes", StringComparison.Ordinal)) + 1;
        var mismatches = new List<string>();

        foreach (string typeKey in typeKeys)
        {
            string startMarker = $"{BlockPrefix}{typeKey} -->";
            IReadOnlyList<string> expected = FieldTable(registry.GetConfigSchema(typeKey));
            string paste = string.Join(Environment.NewLine, [startMarker, .. expected, EndMarker]);
            int[] starts = Enumerable.Range(0, lines.Length)
                .Where(i => string.Equals(lines[i].Trim(), startMarker, StringComparison.Ordinal))
                .ToArray();
            (int StartLine, IReadOnlyList<string> Body)? block = ClaimCheck.Section(lines, startMarker, EndMarker);
            if (block is null)
            {
                mismatches.Add($"{GuidePath}:{Math.Max(nodesLine, 1)}: no field table for {typeKey}; paste:{Environment.NewLine}{paste}");
                continue;
            }

            if (starts.Length > 1)
            {
                mismatches.Add($"{GuidePath}:{starts[1] + 1}: a second field table for {typeKey}");
            }

            int differs = FirstDifference(block.Value.Body, expected);
            if (differs >= 0)
            {
                mismatches.Add($"{GuidePath}:{block.Value.StartLine + 1 + differs}: the field table for {typeKey} differs from its schema; paste:{Environment.NewLine}{paste}");
            }
        }

        int found = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].Trim();
            if (!text.StartsWith(BlockPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            found++;
            string typeKey = text[BlockPrefix.Length..].Replace("-->", string.Empty, StringComparison.Ordinal).Trim();
            if (!typeKeys.Contains(typeKey, StringComparer.Ordinal))
            {
                mismatches.Add($"{GuidePath}:{i + 1}: a field table for '{typeKey}', which is not a registered node type");
            }
        }

        ClaimCheck.Report(_output, "node-fields", found, typeKeys.Length, mismatches);
    }

    [Fact]
    public void Text_cells_escape_markdown_and_code_cells_stay_literal()
    {
        var field = new ConfigField(
            "pattern",
            ConfigFieldType.String,
            "Regex_Pattern",
            DefaultValue: @"\d+|x",
            Placeholder: @"y|\d",
            Description: @"Match \.jpe?g$ or *.jpg;*.png or a_b");

        IReadOnlyList<string> table = FieldTable([field]);

        table[2].Should().Be(@"| `pattern` | Regex\_Pattern | no | `\d+\|x` | — | `y\|\d` | Match \\.jpe?g$ or \*.jpg;\*.png or a\_b |");
    }

    // The table the user guide carries for one node: a header, a separator, and one row per field in schema order.
    private static IReadOnlyList<string> FieldTable(IReadOnlyList<ConfigField> schema)
    {
        var rows = new List<string>
        {
            "| Field | Label | Required | Default | Options | Example | Description |",
            "|---|---|---|---|---|---|---|",
        };
        foreach (ConfigField field in schema)
        {
            string[] cells =
            [
                CodeCell(field.Key),
                TextCell(field.Label),
                field.Required ? "yes" : "no",
                string.IsNullOrEmpty(field.DefaultValue) ? None : CodeCell(field.DefaultValue),
                field.Options is null || field.Options.Count == 0 ? None : string.Join(", ", field.Options.Select(CodeCell)),
                string.IsNullOrEmpty(field.Placeholder) ? None : CodeCell(field.Placeholder),
                string.IsNullOrEmpty(field.Description) ? None : TextCell(field.Description),
            ];
            rows.Add($"| {string.Join(" | ", cells)} |");
        }

        return rows;
    }

    // A backslash inside a code span is literal, so only the table's own separator needs escaping there.
    private static string CodeCell(string text) => $"`{text.Replace("|", "\\|", StringComparison.Ordinal)}`";

    // Plain text renders as Markdown: escape the backslash first, then every character Markdown would act on.
    private static string TextCell(string text) =>
        MarkdownSpecials.Aggregate(text, (escaped, special) => escaped.Replace(special, "\\" + special, StringComparison.Ordinal));

    // The index of the first line that differs (actual.Count, the end marker, when the block is short), or -1 when both are equal.
    private static int FirstDifference(IReadOnlyList<string> actual, IReadOnlyList<string> expected)
    {
        for (int i = 0; i < Math.Max(actual.Count, expected.Count); i++)
        {
            if (i >= actual.Count || i >= expected.Count || !string.Equals(actual[i], expected[i], StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
