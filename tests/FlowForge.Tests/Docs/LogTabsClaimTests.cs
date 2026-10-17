using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class LogTabsClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";
    private const string ViewPath = "src/FlowForge.UI/Views/ExecutionLogView.axaml";

    private readonly ITestOutputHelper _output;

    public LogTabsClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guide_names_each_execution_log_tab()
    {
        var mismatches = new List<string>();
        Dictionary<string, int> headers = TabHeaders(XDocument.Load(RepoRoot.Combine(ViewPath.Split('/')), LoadOptions.SetLineInfo), mismatches);
        string[] lines = File.ReadAllLines(RepoRoot.Combine(GuidePath.Split('/')));
        _output.WriteLine($"tab headers: {string.Join(", ", headers.Keys)}");

        int section = Array.FindIndex(lines, l => string.Equals(l.Trim(), "### Execution Log", StringComparison.Ordinal));
        if (section < 0)
        {
            mismatches.Add($"{GuidePath}:1: no '### Execution Log' section");
        }

        int scanned = 0;
        var named = new HashSet<string>(StringComparer.Ordinal);
        for (int i = section + 1; section >= 0 && i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            Match match = TabBullet().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            scanned++;
            string name = match.Groups["name"].Value;
            named.Add(name);
            if (!headers.ContainsKey(name))
            {
                mismatches.Add($"{GuidePath}:{i + 1}: '{name} tab' names no tab of the execution log");
            }
        }

        foreach ((string header, int line) in headers.Where(h => !named.Contains(h.Key)))
        {
            mismatches.Add($"{ViewPath}:{line}: the '{header}' tab has no bullet in the guide's Execution Log section");
        }

        ClaimCheck.Report(_output, "log-tabs", scanned, headers.Count, mismatches);
    }

    // The literal text of the first TextBlock in each tab-strip toggle, with its line in the view.
    private static Dictionary<string, int> TabHeaders(XDocument view, List<string> mismatches)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);
        IEnumerable<XElement> tabs = view.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "ToggleButton", StringComparison.Ordinal)
                && ((string?)e.Attribute("Classes") ?? string.Empty).Split(' ').Contains("outputTab", StringComparer.Ordinal));
        foreach (XElement tab in tabs)
        {
            XElement? text = tab.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, "TextBlock", StringComparison.Ordinal));
            string? header = (string?)text?.Attribute("Text");
            if (text is null || header is null || header.StartsWith('{'))
            {
                mismatches.Add($"{ViewPath}:{((IXmlLineInfo)tab).LineNumber}: a tab has no literal header text");
                continue;
            }

            headers[header] = ((IXmlLineInfo)text).LineNumber;
        }

        if (headers.Count == 0)
        {
            mismatches.Add($"{ViewPath}:1: no tab-strip toggle (Classes=\"outputTab\") found");
        }

        return headers;
    }

    [GeneratedRegex(@"^- \*\*(?<name>[^*]+) tab\*\* — ", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TabBullet();
}
