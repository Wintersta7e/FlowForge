using System.Globalization;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class NodeListClaimTests
{
    private const string ReadmePath = "README.md";
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public NodeListClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Documented_node_types_match_the_registry()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        string[] typeKeys = registry.GetRegisteredTypeKeys().ToArray();
        var mismatches = new List<string>();

        int scanned = CompareReadme(File.ReadAllLines(RepoRoot.Combine(ReadmePath)), typeKeys, mismatches);
        Dictionary<string, string> libraryGroups = typeKeys.ToDictionary(
            registry.GetDisplayName,
            k => NodeLibraryViewModel.CategoryDisplayNames[registry.GetCategoryForTypeKey(k).ToString()],
            StringComparer.Ordinal);
        scanned += CompareGuide(File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md")), libraryGroups, mismatches);

        ClaimCheck.Report(_output, "node-list", scanned, typeKeys.Length, mismatches);
    }

    // The README's "N built-in node types" numerals and its backticked list of node type keys.
    private static int CompareReadme(string[] lines, string[] typeKeys, List<string> mismatches)
    {
        int scanned = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match match in NodeTypeCount().Matches(lines[i]))
            {
                scanned++;
                int count = int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture);
                if (count != typeKeys.Length)
                {
                    mismatches.Add($"{ReadmePath}:{i + 1}: states {count} built-in node types; the registry has {typeKeys.Length}");
                }
            }
        }

        int listLine = Array.FindIndex(lines, l => l.Contains("built-in node types:", StringComparison.Ordinal));
        if (listLine < 0)
        {
            mismatches.Add($"{ReadmePath}:1: no line contains 'built-in node types:'");
            return scanned;
        }

        var listed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = listLine + 1; i < lines.Length && lines[i].StartsWith("  - ", StringComparison.Ordinal); i++)
        {
            foreach (Match match in BacktickedName().Matches(lines[i]))
            {
                scanned++;
                string name = match.Groups["name"].Value;
                listed.Add(name);
                if (!typeKeys.Contains(name, StringComparer.Ordinal))
                {
                    mismatches.Add($"{ReadmePath}:{i + 1}: '{name}' is not a registered node type");
                }
            }
        }

        foreach (string typeKey in typeKeys.Where(k => !listed.Contains(k)))
        {
            mismatches.Add($"{ReadmePath}:{listLine + 1}: registered node type '{typeKey}' is not listed");
        }

        return scanned;
    }

    // The user guide's "### <display name> (<node library group>)" headings under "## Nodes".
    private static int CompareGuide(string[] lines, Dictionary<string, string> libraryGroups, List<string> mismatches)
    {
        int nodes = Array.FindIndex(lines, l => string.Equals(l.Trim(), "## Nodes", StringComparison.Ordinal));
        if (nodes < 0)
        {
            mismatches.Add($"{GuidePath}:1: no '## Nodes' section");
            return 0;
        }

        int scanned = 0;
        var headed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = nodes + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            Match match = NodeHeading().Match(lines[i].TrimEnd());
            if (!match.Success)
            {
                continue;
            }

            scanned++;
            string name = match.Groups["name"].Value;
            string category = match.Groups["cat"].Value;
            headed.Add(name);
            if (!libraryGroups.TryGetValue(name, out string? group))
            {
                mismatches.Add($"{GuidePath}:{i + 1}: heading '{name}' is not a node's display name");
            }
            else if (!string.Equals(category, group, StringComparison.Ordinal))
            {
                mismatches.Add($"{GuidePath}:{i + 1}: heading '{name}' names the group '{category}'; the node library lists it under '{group}'");
            }
        }

        foreach (string name in libraryGroups.Keys.Where(n => !headed.Contains(n)).Order(StringComparer.Ordinal))
        {
            mismatches.Add($"{GuidePath}:{nodes + 1}: no heading for the node '{name}'");
        }

        return scanned;
    }

    [GeneratedRegex(@"(?<count>\d+) built-in node types", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NodeTypeCount();

    [GeneratedRegex(@"`(?<name>[^`]+)`", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BacktickedName();

    [GeneratedRegex(@"^### (?<name>.+) \((?<cat>[^()]+)\)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NodeHeading();
}
