using Avalonia.Headless.XUnit;
using FlowForge.Tests.Helpers;
using FlowForge.Tests.Visual;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// Every font family the desktop app ships, read from the font assets themselves, is credited in the third-party
/// notices' font table and named in the README's licence line.
/// </summary>
[Collection(VisualCollection.Name)]
public class FontNoticesClaimTests
{
    private const string NoticesPath = "THIRD-PARTY-NOTICES.md";
    private const string ReadmePath = "README.md";

    private readonly ITestOutputHelper _output;

    public FontNoticesClaimTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Every_shipped_font_family_is_credited()
    {
        var mismatches = new List<string>();
        List<string> families = ThemePaintTests.BundledFamilies(_output);
        List<(int Line, string Name, string[] Styles)> rows = FontRows(File.ReadAllLines(RepoRoot.Combine(NoticesPath)));
        string[] readme = File.ReadAllLines(RepoRoot.Combine(ReadmePath));
        int licence = Array.FindIndex(readme, l => l.Contains("OFL-1.1 font families", StringComparison.Ordinal));

        foreach (string family in families)
        {
            // A row credits a family by its name, or by its name and one of its listed styles ("Inter" + "Light").
            (int Line, string Name, string[] Styles) row = rows.FirstOrDefault(r =>
                string.Equals(r.Name, family, StringComparison.Ordinal)
                || r.Styles.Any(style => string.Equals($"{r.Name} {style}", family, StringComparison.Ordinal)));
            if (row.Name is null)
            {
                mismatches.Add($"{NoticesPath}:{(rows.Count > 0 ? rows[0].Line : 1)}: the font table does not credit '{family}'");
            }
            else if (licence < 0 || !readme[licence].Contains(row.Name, StringComparison.Ordinal))
            {
                mismatches.Add($"{ReadmePath}:{licence + 1}: the licence line does not name '{row.Name}'");
            }
        }

        ClaimCheck.Report(_output, "font-notices", families.Count, 4, mismatches);
    }

    // The font table's rows under "## Fonts", as (line, name, styles):
    // "| Inter (Thin, Light) … |" gives ("Inter", ["Thin", "Light"]).
    private static List<(int Line, string Name, string[] Styles)> FontRows(string[] lines)
    {
        var rows = new List<(int Line, string Name, string[] Styles)>();
        int start = Array.FindIndex(lines, l => string.Equals(l.Trim(), "## Fonts", StringComparison.Ordinal));
        for (int i = start + 1; start >= 0 && i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            string[] cells = lines[i].Split('|');
            string font = cells.Length < 3 || !lines[i].StartsWith('|') ? string.Empty : cells[1].Trim();
            if (font.Length == 0 || string.Equals(font, "Font", StringComparison.Ordinal) || font.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            int open = font.IndexOf(" (", StringComparison.Ordinal);
            int close = font.IndexOf(')', StringComparison.Ordinal);

            string name = open < 0 ? font : font[..open];
            string[] styles = open < 0 || close < open
                ? []
                : font[(open + 2)..close].Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            rows.Add((i + 1, name, styles));
        }

        return rows;
    }
}
