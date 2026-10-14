using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Repository;

public class DependabotConfigTests
{
    private const string VersionCapNameLine = "      - dependency-name: \"";
    private const string VersionCapRangeLine = "        versions: ";

    // The pinned config minus its version-cap ignore entries, which are checked
    // against the project files instead so a cap goes when its package goes.
    private const string ExpectedWithoutVersionCaps = """
        version: 2
        updates:
          - package-ecosystem: nuget
            directory: /
            schedule:
              interval: weekly
            open-pull-requests-limit: 5
            groups:
              avalonia:
                patterns: ["Avalonia*"]
              microsoft-extensions:
                patterns: ["Microsoft.Extensions.*"]
            ignore:
              - dependency-name: "Avalonia*"
                update-types: ["version-update:semver-major", "version-update:semver-minor"]
              - dependency-name: "Nodify.Avalonia"
                update-types: ["version-update:semver-major"]
          - package-ecosystem: github-actions
            directory: /
            schedule:
              interval: weekly
        """;

    private readonly ITestOutputHelper _output;

    public DependabotConfigTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Dependabot_config_matches_the_pinned_groups_and_ceilings()
    {
        string text = (await File.ReadAllTextAsync(RepoRoot.Combine(".github", "dependabot.yml"))).ReplaceLineEndings("\n");
        string[] lines = text.Split('\n');

        List<string> kept = [];
        List<string> versionCaps = [];
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(VersionCapNameLine, StringComparison.Ordinal)
                && i + 1 < lines.Length
                && lines[i + 1].StartsWith(VersionCapRangeLine, StringComparison.Ordinal))
            {
                versionCaps.Add(lines[i][VersionCapNameLine.Length..].TrimEnd('"'));
                i++;
                continue;
            }

            kept.Add(lines[i]);
        }

        List<string> projectFiles = [RepoRoot.Combine("Directory.Build.props")];
        foreach (string root in new[] { RepoRoot.Combine("src"), RepoRoot.Combine("tests") })
        {
            projectFiles.AddRange(Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(Path.GetRelativePath(RepoRoot.FullPath, file))));
        }

        List<string> projectTexts = [];
        foreach (string file in projectFiles)
        {
            projectTexts.Add(await File.ReadAllTextAsync(file));
        }

        _output.WriteLine($"[dependabot] project-files={projectFiles.Count} version-caps={versionCaps.Count}");

        string.Join("\n", kept).Should().Be(ExpectedWithoutVersionCaps.ReplaceLineEndings("\n") + "\n");
        projectFiles.Should().NotBeEmpty();
        versionCaps.Where(name => !projectTexts.Exists(t => t.Contains($"Include=\"{name}\"", StringComparison.Ordinal)))
            .Should().BeEmpty("a version cap must be deleted when its package is no longer referenced");
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");
}
