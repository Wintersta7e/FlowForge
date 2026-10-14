using System.Xml.Linq;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

[Collection(VisualCollection.Name)]
public sealed class AvaloniaLockstepTests
{
    private static readonly string[][] Projects =
    [
        ["src", "FlowForge.UI", "FlowForge.UI.csproj"],
        ["tests", "FlowForge.Tests", "FlowForge.Tests.csproj"],
    ];

    private readonly ITestOutputHelper _output;

    public AvaloniaLockstepTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Every_Avalonia_package_carries_one_version()
    {
        var counts = new List<(string File, int Count)>();
        var versions = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string[] parts in Projects)
        {
            string file = string.Join('/', parts);
            List<(string Include, string Version)> references = AvaloniaReferences(RepoRoot.Combine(parts));
            foreach ((string include, string version) in references)
            {
                _output.WriteLine($"{file} {include} {version}");
                versions.Add(version);
            }

            _output.WriteLine($"{file} count={references.Count}");
            counts.Add((file, references.Count));
        }

        _output.WriteLine($"avalonia-references={counts.Sum(c => c.Count)}");
        _output.WriteLine($"versions={string.Join(", ", versions)}");

        foreach ((string file, int count) in counts)
        {
            count.Should().BeGreaterThan(0, "{0} must reference at least one Avalonia package", file);
        }

        versions.Should().ContainSingle("every Avalonia package moves in one bump; found {0}", string.Join(", ", versions));
    }

    // Every PackageReference whose Include starts with "Avalonia", with its Version attribute or child element.
    private static List<(string Include, string Version)> AvaloniaReferences(string project) =>
        XDocument.Load(project).Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "PackageReference", StringComparison.Ordinal))
            .Select(e => (
                Include: (string?)e.Attribute("Include") ?? string.Empty,
                Version: (string?)e.Attribute("Version")
                    ?? (string?)e.Elements().FirstOrDefault(c => string.Equals(c.Name.LocalName, "Version", StringComparison.Ordinal))
                    ?? "(none)"))
            .Where(r => r.Include.StartsWith("Avalonia", StringComparison.Ordinal))
            .ToList();
}
