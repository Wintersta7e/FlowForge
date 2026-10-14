using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class LinksClaimTests
{
    private static readonly string[] Documents =
        ["README.md", "docs/USER_GUIDE.md", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md", "samples/README.md"];

    private static readonly string[] ExternalPrefixes = ["#", "http://", "https://", "mailto:"];

    private readonly ITestOutputHelper _output;

    public LinksClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Relative_links_resolve()
    {
        var mismatches = new List<string>();
        int scanned = 0;
        foreach (string document in Documents)
        {
            string path = RepoRoot.Combine(document.Split('/'));
            string directory = Path.GetDirectoryName(path)!;
            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                IEnumerable<string> targets = MarkdownLink().Matches(lines[i])
                    .Concat(ImageSource().Matches(lines[i]))
                    .Select(m => m.Groups["target"].Value);
                foreach (string target in targets)
                {
                    if (ExternalPrefixes.Any(prefix => target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    scanned++;
                    int fragment = target.IndexOf('#', StringComparison.Ordinal);
                    string relative = (fragment < 0 ? target : target[..fragment]).Replace('/', Path.DirectorySeparatorChar);
                    string resolved = Path.GetFullPath(Path.Combine(directory, relative));
                    if (!File.Exists(resolved) && !Directory.Exists(resolved))
                    {
                        mismatches.Add($"{document}:{i + 1}: link target '{target}' does not exist");
                    }
                }
            }
        }

        ClaimCheck.Report(_output, "links", scanned, 1, mismatches);
    }

    [GeneratedRegex(@"\]\((?<target>[^)\s]+)(\s+""[^""]*"")?\)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"<img\b[^>]*\bsrc=""(?<target>[^""]+)""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ImageSource();
}
