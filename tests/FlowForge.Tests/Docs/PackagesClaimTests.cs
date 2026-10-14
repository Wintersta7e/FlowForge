using System.Text.RegularExpressions;
using System.Xml.Linq;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class PackagesClaimTests
{
    private const string ReadmePath = "README.md";
    private const string NoticesPath = "THIRD-PARTY-NOTICES.md";
    private const string TestsProjectPath = "tests/FlowForge.Tests/FlowForge.Tests.csproj";
    private const string LayoutTestsPrefix = "FlowForge.Tests/   #";

    private readonly ITestOutputHelper _output;

    public PackagesClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Documented_packages_match_the_project_references()
    {
        string[] projects = Directory.EnumerateDirectories(RepoRoot.Combine("src"))
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.csproj"))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var references = projects
            .SelectMany(project => PackageReferences(project).Select(package => (Project: RelativePath(project), Package: package)))
            .ToList();
        var mismatches = new List<string>();

        string[] readme = File.ReadAllLines(RepoRoot.Combine(ReadmePath));
        List<(int Line, string Layer, string Choice)> stack = StackRows(readme, mismatches);
        CompareList(NoticesPath, NoticesList(File.ReadAllLines(RepoRoot.Combine(NoticesPath)), mismatches), references, mismatches);
        CompareList(ReadmePath, StackList(readme, stack), references, mismatches);
        CompareTestPackages(readme, stack, mismatches);

        ClaimCheck.Report(_output, "packages", references.Count, projects.Length, mismatches);
    }

    // Every reference must be covered by a listed name, and every listed name must cover a reference.
    private static void CompareList(
        string document,
        (int AnchorLine, List<(int Line, string Name)> Names) list,
        List<(string Project, string Package)> references,
        List<string> mismatches)
    {
        foreach ((string project, string package) in references)
        {
            if (!list.Names.Exists(listed => Covers(listed.Name, package)))
            {
                mismatches.Add($"{document}:{list.AnchorLine}: {package} ({project}) is not covered by any listed name");
            }
        }

        foreach ((int line, string name) in list.Names)
        {
            if (!references.Exists(reference => Covers(name, reference.Package)))
            {
                mismatches.Add($"{document}:{line}: {name} matches no package reference under src/");
            }
        }
    }

    // A name covers itself and its dotted family; a name ending ".*" covers its prefix.
    private static bool Covers(string name, string package) =>
        name.EndsWith(".*", StringComparison.Ordinal)
            ? package.StartsWith(name[..^1], StringComparison.Ordinal)
            : string.Equals(package, name, StringComparison.Ordinal) || package.StartsWith(name + ".", StringComparison.Ordinal);

    // The notices' "- Name (licence)" lines after "Notable runtime dependencies:".
    private static (int AnchorLine, List<(int Line, string Name)> Names) NoticesList(string[] lines, List<string> mismatches)
    {
        var names = new List<(int Line, string Name)>();
        int anchor = Array.FindIndex(lines, l => l.Contains("Notable runtime dependencies:", StringComparison.Ordinal));
        if (anchor < 0)
        {
            mismatches.Add($"{NoticesPath}:1: no line contains 'Notable runtime dependencies:'");
            return (1, names);
        }

        int i = anchor + 1;
        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i]))
        {
            i++;
        }

        for (; i < lines.Length && lines[i].StartsWith("- ", StringComparison.Ordinal); i++)
        {
            string entry = lines[i][2..];
            int licence = entry.IndexOf(" (", StringComparison.Ordinal);
            names.Add((i + 1, (licence < 0 ? entry : entry[..licence]).Trim()));
        }

        return (anchor + 1, names);
    }

    // The backticked names in the Choice cell of every Stack row except Tests.
    private static (int AnchorLine, List<(int Line, string Name)> Names) StackList(string[] readme, List<(int Line, string Layer, string Choice)> stack)
    {
        int anchor = Array.FindIndex(readme, l => string.Equals(l.Trim(), "## Stack", StringComparison.Ordinal)) + 1;
        var names = new List<(int Line, string Name)>();
        foreach ((int line, string layer, string choice) in stack)
        {
            if (string.Equals(layer, "Tests", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in BacktickedName().Matches(choice))
            {
                names.Add((line, match.Groups["name"].Value));
            }
        }

        return (Math.Max(anchor, 1), names);
    }

    // The data rows of the README's Stack table: line, Layer cell and Choice cell.
    private static List<(int Line, string Layer, string Choice)> StackRows(string[] readme, List<string> mismatches)
    {
        var rows = new List<(int Line, string Layer, string Choice)>();
        int start = Array.FindIndex(readme, l => string.Equals(l.Trim(), "## Stack", StringComparison.Ordinal));
        if (start < 0)
        {
            mismatches.Add($"{ReadmePath}:1: no '## Stack' section");
            return rows;
        }

        for (int i = start + 1; i < readme.Length && !readme[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            string[] cells = readme[i].Split('|');
            if (!readme[i].StartsWith('|') || cells.Length < 4)
            {
                continue;
            }

            string layer = cells[1].Trim();
            if (string.Equals(layer, "Layer", StringComparison.Ordinal) || layer.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add((i + 1, layer, cells[2].Trim()));
        }

        return rows;
    }

    // The test libraries the README names (Stack's Tests row and the layout comment) must be test-project references.
    private static void CompareTestPackages(string[] readme, List<(int Line, string Layer, string Choice)> stack, List<string> mismatches)
    {
        var testPackages = PackageReferences(RepoRoot.Combine(TestsProjectPath.Split('/'))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = new List<(int Line, string Text)>();

        int testsRow = stack.FindIndex(row => string.Equals(row.Layer, "Tests", StringComparison.Ordinal));
        if (testsRow < 0)
        {
            mismatches.Add($"{ReadmePath}:1: the Stack table has no Tests row");
        }
        else
        {
            named.Add((stack[testsRow].Line, stack[testsRow].Choice));
        }

        int layout = Array.FindIndex(readme, l => l.Contains(LayoutTestsPrefix, StringComparison.Ordinal));
        if (layout < 0)
        {
            mismatches.Add($"{ReadmePath}:1: no layout line contains '{LayoutTestsPrefix}'");
        }
        else
        {
            string text = readme[layout];
            named.Add((layout + 1, text[(text.IndexOf(LayoutTestsPrefix, StringComparison.Ordinal) + LayoutTestsPrefix.Length)..]));
        }

        foreach ((int line, string text) in named)
        {
            foreach (string name in text.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!testPackages.Contains(name))
                {
                    mismatches.Add($"{ReadmePath}:{line}: {name} is not a package reference of {TestsProjectPath}");
                }
            }
        }
    }

    private static IEnumerable<string> PackageReferences(string project) =>
        XDocument.Load(project).Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "PackageReference", StringComparison.Ordinal))
            .Select(e => (string?)e.Attribute("Include"))
            .OfType<string>()
            .ToList();

    private static string RelativePath(string path) => Path.GetRelativePath(RepoRoot.FullPath, path).Replace('\\', '/');

    [GeneratedRegex(@"`(?<name>[^`]+)`", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BacktickedName();
}
