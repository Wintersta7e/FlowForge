using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class NoNetworkClaimTests
{
    private static readonly string[] NetworkApis =
        ["System.Net.Http", "HttpClient", "WebRequest", "System.Net.Sockets", "TcpClient"];

    private readonly ITestOutputHelper _output;

    public NoNetworkClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Source_uses_no_network_api()
    {
        string srcDir = RepoRoot.Combine("src");
        int projects = Directory.EnumerateDirectories(srcDir).Sum(dir => Directory.EnumerateFiles(dir, "*.csproj").Count());
        string[] sources = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(srcDir, file)
                .Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        var mismatches = new List<string>();
        foreach (string source in sources)
        {
            string relative = Path.GetRelativePath(RepoRoot.FullPath, source).Replace('\\', '/');
            string[] lines = File.ReadAllLines(source);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string api in NetworkApis.Where(api => lines[i].Contains(api, StringComparison.Ordinal)))
                {
                    mismatches.Add($"{relative}:{i + 1}: uses {api}");
                }
            }
        }

        ClaimCheck.Report(_output, "no-network", sources.Length, projects, mismatches);
    }
}
