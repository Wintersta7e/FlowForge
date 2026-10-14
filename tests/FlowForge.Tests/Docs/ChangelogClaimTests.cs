using System.Security.Cryptography;
using System.Text;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class ChangelogClaimTests
{
    // SHA-256 of CHANGELOG.md from "## [2.0.0]" to the end, line endings normalised to \n so both checkouts agree.
    private const string ReleasedSectionsSha256 = "8443662c5854d54f3a27abffbf915748fd1e6ee8b0bea6ed8b8a93e225e3c1e0";

    private readonly ITestOutputHelper _output;

    public ChangelogClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Released_changelog_sections_are_unchanged()
    {
        string text = File.ReadAllText(RepoRoot.Combine("CHANGELOG.md")).Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = text.IndexOf("## [2.0.0]", StringComparison.Ordinal);
        byte[] released = start < 0 ? [] : Encoding.UTF8.GetBytes(text[start..]);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(released));
        _output.WriteLine($"[changelog] released-bytes={released.Length} sha256={sha256}");

        released.Length.Should().BeGreaterThan(0, "CHANGELOG.md must hold the released 2.0.0 section");
        sha256.Should().Be(ReleasedSectionsSha256, "the sections at and below '## [2.0.0]' record past releases and never change");
    }
}
