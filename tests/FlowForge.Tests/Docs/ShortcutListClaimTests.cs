using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FlowForge.Tests.Helpers;
using FlowForge.Tests.Visual;
using FlowForge.UI.Views;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

[Collection(VisualCollection.Name)]
public partial class ShortcutListClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";
    private const string StartMarker = "<!-- shortcuts -->";
    private const string EndMarker = "<!-- /shortcuts -->";

    private readonly ITestOutputHelper _output;

    public ShortcutListClaimTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void UserGuideShortcutList_MatchesHelpDialog()
    {
        IReadOnlyList<(string Key, string Description)> shown = ShownRows();
        var expected = shown.Select(row => $"- **{row.Key}** — {row.Description}").ToList();
        string paste = string.Join(Environment.NewLine, [StartMarker, .. expected, EndMarker]);
        string[] lines = File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        var mismatches = new List<string>();
        int scanned = 0;

        (int StartLine, IReadOnlyList<string> Body)? block = ClaimCheck.Section(lines, StartMarker, EndMarker);
        if (block is null)
        {
            int editorLine = Array.FindIndex(lines, l => string.Equals(l.Trim(), "## The Editor", StringComparison.Ordinal)) + 1;
            mismatches.Add($"{GuidePath}:{Math.Max(editorLine, 1)}: no shortcut list; paste:{Environment.NewLine}{paste}");
        }
        else
        {
            IReadOnlyList<string> body = block.Value.Body;
            scanned = body.Count(line => ShortcutRow().IsMatch(line));
            for (int i = 0; i < Math.Max(body.Count, expected.Count); i++)
            {
                if (i >= body.Count || i >= expected.Count || !string.Equals(body[i], expected[i], StringComparison.Ordinal))
                {
                    mismatches.Add($"{GuidePath}:{block.Value.StartLine + 1 + i}: the shortcut list differs from the shortcuts dialog; paste:{Environment.NewLine}{paste}");
                    break;
                }
            }
        }

        ClaimCheck.Report(_output, "shortcuts", scanned, shown.Count, mismatches);
    }

    // The rows of the shortcuts dialog as it is shown.
    private static IReadOnlyList<(string Key, string Description)> ShownRows()
    {
        var dialog = new ShortcutsWindow();
        dialog.Show();
        try
        {
            return ShortcutRows.Read(dialog);
        }
        finally
        {
            dialog.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [GeneratedRegex(@"^- \*\*[^*]+\*\* — .+$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShortcutRow();
}
