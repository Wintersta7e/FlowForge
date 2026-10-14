using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Repository;

// Members deleted because nothing read them. A whole-identifier scan of src/ keeps them from
// coming back unnoticed.
public partial class RemovedMembersTests
{
    private static readonly string[] RemovedNames =
    [
        "MaxConcurrency",
        "MaxAllowedConcurrency",
        "DefaultInputFolder",
        "DefaultOutputFolder",
        "SettingsFilePath",
        "GetCategory",
        "ReportProgress",
        "FlushInterval",
        "_flushTimer",
        "EnsureFlushTimerRunning",
        "StopFlushTimer",
        "OnFlushTimerTick",
        "CategoryBrush",
        "HeaderBrush",
        "NodeBorderBrush",
        "NodeBackground",
        "GetCategoryColor",
        "GetCategoryBrush",
        "BuildHeaderBrush",
        "BuildNodeBackground",
        "ConnectorBrush",
        "BadgeForeground",
        "BadgeBackground",
        "_badgeForeground",
        "_badgeBackground",
        "ForgeSourceDim",
        "ForgeTransformDim",
        "ForgeOutputDim",
        "ForgeSource",
        "ForgeTransform",
        "ForgeOutput",
        "ForgeSourceColor",
        "ForgeTransformColor",
        "ForgeOutputColor",
    ];

    // Text that must not come back in one file; other files may still use it.
    private static readonly (string Path, string Text)[] RemovedText =
    [
        ("src/FlowForge.Core/Execution/NodeRegistry.cs", "bool configure"),
        ("src/FlowForge.UI/ViewModels/ExecutionLogViewModel.cs", "DispatcherTimer"),
    ];

    private readonly ITestOutputHelper _output;

    public RemovedMembersTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Removed_members_have_no_references_in_src()
    {
        var names = new HashSet<string>(RemovedNames, StringComparer.Ordinal);
        string[] files = Directory.EnumerateFiles(RepoRoot.Combine("src"), "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot.FullPath, f).Replace('\\', '/'))
            .Where(f => !f.Split('/').Any(s => s is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var hits = new List<string>();
        foreach (string file in files)
        {
            string[] lines = await File.ReadAllLinesAsync(RepoRoot.Combine(file.Split('/')));
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match identifier in Identifier().Matches(lines[i]))
                {
                    if (names.Contains(identifier.Value))
                    {
                        hits.Add($"{file}:{i + 1} {identifier.Value}");
                    }
                }
            }
        }

        foreach ((string path, string text) in RemovedText)
        {
            string content = await File.ReadAllTextAsync(RepoRoot.Combine(path.Split('/')));
            if (content.Contains(text, StringComparison.Ordinal))
            {
                hits.Add($"{path} contains '{text}'");
            }
        }

        _output.WriteLine($"[removed-members] files={files.Length} names={RemovedNames.Length} hits={hits.Count}");
        foreach (string hit in hits)
        {
            _output.WriteLine(hit);
        }

        files.Should().NotBeEmpty("a scan of zero files proves nothing");
        hits.Should().BeEmpty("a deleted member came back, or the name was reused on purpose — then drop it from RemovedNames in the same commit");
    }

    [GeneratedRegex(@"[\p{L}\p{Nd}_]+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Identifier();
}
