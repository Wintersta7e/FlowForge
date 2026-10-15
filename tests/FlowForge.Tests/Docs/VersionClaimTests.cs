using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class VersionClaimTests
{
    private const string ChangelogPath = "CHANGELOG.md";
    private const string DevSuffix = "-dev";

    private readonly ITestOutputHelper _output;

    public VersionClaimTests(ITestOutputHelper output) => _output = output;

    // A development version is still unreleased: its changes sit under [Unreleased] and no section
    // carries its number yet. A release version has its own dated section. The toolbar's version
    // label is built from the desktop app's assembly version, whose major must be the build's.
    [Fact]
    public void Changelog_sections_match_the_build_version()
    {
        string version = XDocument.Load(RepoRoot.Combine("Directory.Build.props")).Descendants("Version").Single().Value;
        string[] lines = File.ReadAllLines(RepoRoot.Combine(ChangelogPath));
        var mismatches = new List<string>();

        if (version.EndsWith(DevSuffix, StringComparison.Ordinal))
        {
            string released = version[..^DevSuffix.Length];
            if (!Array.Exists(lines, l => string.Equals(l.Trim(), "## [Unreleased]", StringComparison.Ordinal)))
            {
                mismatches.Add($"{ChangelogPath}:1: no '## [Unreleased]' section while the version is {version}");
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith($"## [{released}]", StringComparison.Ordinal))
                {
                    mismatches.Add($"{ChangelogPath}:{i + 1}: section '{lines[i].Trim()}' exists while the version is {version}");
                }
            }
        }
        else
        {
            string heading = $@"^## \[{Regex.Escape(version)}\] - \d{{4}}-\d{{2}}-\d{{2}}$";
            if (!Array.Exists(lines, l => Regex.IsMatch(l.Trim(), heading, RegexOptions.None, TimeSpan.FromSeconds(1))))
            {
                mismatches.Add($"{ChangelogPath}:1: no '## [{version}] - YYYY-MM-DD' section for the release version {version}");
            }
        }

        int major = int.Parse(version.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture);
        Version? labelVersion = typeof(MainWindowViewModel).Assembly.GetName().Version;
        if (labelVersion?.Major != major)
        {
            mismatches.Add($"FlowForge.UI: the version label's assembly version {labelVersion} does not have the build version {version}'s major {major}");
        }

        ClaimCheck.Report(_output, "version", 2, 2, mismatches);
    }
}
