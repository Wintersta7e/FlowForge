using System.Text.RegularExpressions;
using System.Xml.Linq;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class PlatformsClaimTests
{
    private const string CiPath = ".github/workflows/ci.yml";
    private const string ReadmePath = "README.md";
    private const string StartMarker = "<!-- platforms -->";
    private const string EndMarker = "<!-- /platforms -->";
    private static readonly string[] KnownSystems = ["Windows", "Linux", "macOS"];
    private static readonly string[] DownloadDocuments = [ReadmePath, "docs/USER_GUIDE.md"];

    private readonly ITestOutputHelper _output;

    public PlatformsClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Documented_platforms_match_the_ci_legs()
    {
        var mismatches = new List<string>();
        HashSet<string> ciSystems = CiSystems(mismatches);
        ComparePlatformSentence(ciSystems, mismatches);
        CompareDownloadLines(mismatches);

        ClaimCheck.Report(_output, "platforms", ciSystems.Count, 2, mismatches);
    }

    // The operating systems the CI workflow builds and tests on, from its runs-on values.
    private static HashSet<string> CiSystems(List<string> mismatches)
    {
        string[] lines = File.ReadAllLines(RepoRoot.Combine(CiPath.Split('/')));
        var systems = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            Match match = RunsOn().Match(lines[i]);
            if (!match.Success)
            {
                continue;
            }

            string value = match.Groups["value"].Value;
            string? system = value switch
            {
                _ when value.StartsWith("ubuntu-", StringComparison.Ordinal) => "Linux",
                _ when value.StartsWith("windows-", StringComparison.Ordinal) => "Windows",
                _ when value.StartsWith("macos-", StringComparison.Ordinal) => "macOS",
                _ => null,
            };
            if (system is null)
            {
                mismatches.Add($"{CiPath}:{i + 1}: runs-on value '{value}' names no known operating system");
            }
            else
            {
                systems.Add(system);
            }
        }

        return systems;
    }

    // The README names every CI system as tested, and marks any other system it names "untested".
    private static void ComparePlatformSentence(HashSet<string> ciSystems, List<string> mismatches)
    {
        string[] lines = File.ReadAllLines(RepoRoot.Combine(ReadmePath));
        int index = Array.FindIndex(lines, l => l.Contains(StartMarker, StringComparison.Ordinal) && l.Contains(EndMarker, StringComparison.Ordinal));
        if (index < 0)
        {
            mismatches.Add($"{ReadmePath}:1: no line holds both '{StartMarker}' and '{EndMarker}'");
            return;
        }

        string line = lines[index];
        int start = line.IndexOf(StartMarker, StringComparison.Ordinal) + StartMarker.Length;
        int end = line.IndexOf(EndMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            mismatches.Add($"{ReadmePath}:{index + 1}: '{EndMarker}' comes before '{StartMarker}'");
            return;
        }

        string[] clauses = line[start..end].Split(';');
        foreach (string system in KnownSystems)
        {
            string[] naming = clauses.Where(c => c.Contains(system, StringComparison.Ordinal)).ToArray();
            bool untested = Array.Exists(naming, c => c.Contains("untested", StringComparison.Ordinal));
            if (ciSystems.Contains(system) && (naming.Length == 0 || untested))
            {
                mismatches.Add($"{ReadmePath}:{index + 1}: {system} is built and tested in CI but not named as tested");
            }
            else if (!ciSystems.Contains(system) && naming.Length > 0 && !untested)
            {
                mismatches.Add($"{ReadmePath}:{index + 1}: {system} has no CI leg, so its clause must say 'untested'");
            }
        }
    }

    // A development version has no release to download; a release version names the release's zip.
    private static void CompareDownloadLines(List<string> mismatches)
    {
        string version = XDocument.Load(RepoRoot.Combine("Directory.Build.props")).Descendants("Version").Single().Value;
        bool development = version.EndsWith("-dev", StringComparison.Ordinal);
        string zipName = ZipName().Match(File.ReadAllText(RepoRoot.Combine(".github", "workflows", "release.yml"))).Value;

        foreach (string path in DownloadDocuments)
        {
            string[] lines = File.ReadAllLines(RepoRoot.Combine(path.Split('/')));
            if (development)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("/releases", StringComparison.Ordinal))
                    {
                        mismatches.Add($"{path}:{i + 1}: links '/releases' while the version is {version}");
                    }

                    if (lines[i].Contains("release zip", StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add($"{path}:{i + 1}: mentions a release zip while the version is {version}");
                    }
                }
            }
            else if (zipName.Length == 0 || !Array.Exists(lines, l => l.Contains("/releases", StringComparison.Ordinal) && l.Contains(zipName, StringComparison.Ordinal)))
            {
                mismatches.Add($"{path}:1: no download line links '/releases' and names '{zipName}' for the release version {version}");
            }
        }
    }

    [GeneratedRegex(@"^\s*runs-on:\s*(?<value>\S.*?)\s*$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RunsOn();

    [GeneratedRegex(@"FlowForge-[\w-]+\.zip", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ZipName();
}
