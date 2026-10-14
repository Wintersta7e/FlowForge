using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

[Collection(VisualCollection.Name)]
public sealed partial class VisualSourceRulesTests
{
    private readonly ITestOutputHelper _output;

    public VisualSourceRulesTests(ITestOutputHelper output) => _output = output;

    // Visual tests reach animated states by condition, never by waiting on the clock, and no
    // test resolves from the running app's container, whose settings live at the user's path.
    [Fact]
    public async Task Visual_tests_never_sleep_and_tests_never_use_the_app_container()
    {
        List<string> visualFiles = SourceFiles("tests", "FlowForge.Tests", "Visual");
        List<string> testFiles = SourceFiles("tests");
        List<string> sleepHits = await HitsAsync(visualFiles, SleepCall());
        List<string> appContainerHits = await HitsAsync(testFiles, AppContainer());

        sleepHits.ForEach(hit => _output.WriteLine($"sleep {hit}"));
        appContainerHits.ForEach(hit => _output.WriteLine($"app-services {hit}"));
        _output.WriteLine($"visual-files={visualFiles.Count} sleep-hits={sleepHits.Count}");
        _output.WriteLine($"test-files={testFiles.Count} app-services-hits={appContainerHits.Count}");

        visualFiles.Should().NotBeEmpty();
        testFiles.Should().NotBeEmpty();
        sleepHits.Should().BeEmpty("visual tests pump until a condition holds instead of sleeping");
        appContainerHits.Should().BeEmpty("tests build their own services instead of the app's container");
    }

    // Every .cs file under a repo-relative directory, build output excluded, as repo-relative paths.
    private static List<string> SourceFiles(params string[] directory) =>
        Directory.EnumerateFiles(RepoRoot.Combine(directory), "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(RepoRoot.FullPath, file))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static async Task<List<string>> HitsAsync(IReadOnlyList<string> files, Regex pattern)
    {
        var hits = new List<string>();
        foreach (string relative in files)
        {
            string[] lines = await File.ReadAllLinesAsync(RepoRoot.Combine(relative));
            for (int i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    hits.Add($"{relative.Replace('\\', '/')}:{i + 1}");
                }
            }
        }

        return hits;
    }

    // Both patterns are written so that this file's own source text cannot match them.
    [GeneratedRegex(@"Thread\s*\.\s*Sleep|Task\s*\.\s*Delay", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SleepCall();

    [GeneratedRegex(@"App\s*\.\s*Services", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AppContainer();
}
