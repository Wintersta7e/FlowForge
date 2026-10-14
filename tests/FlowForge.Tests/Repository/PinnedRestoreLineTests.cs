using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Repository;

public class PinnedRestoreLineTests
{
    private const string RestoreLine = "dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -p:TreatWarningsAsErrors=true";

    private readonly ITestOutputHelper _output;

    public PinnedRestoreLineTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Ci_audit_job_and_local_gate_use_the_same_restore_line()
    {
        string[] files =
        [
            RepoRoot.Combine(".github", "workflows", "ci.yml"),
            RepoRoot.Combine("scripts", "gate.sh"),
        ];

        List<string> unmatched = [];
        foreach (string file in files)
        {
            string[] lines = await File.ReadAllLinesAsync(file);
            if (!lines.Any(IsRestoreLine))
            {
                unmatched.Add(Path.GetFileName(file));
            }
        }

        _output.WriteLine($"[restore-line] scanned={files.Length} matched={files.Length - unmatched.Count}");
        unmatched.Should().BeEmpty("CI's audit job and the local gate must restore with the same pinned line");
    }

    // Accepts the bare line, "run: <line>" under a named step and "- run: <line>";
    // an added or changed flag does not match.
    private static bool IsRestoreLine(string line)
    {
        string text = line.Trim();
        if (text.StartsWith("- ", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        if (text.StartsWith("run: ", StringComparison.Ordinal))
        {
            text = text["run: ".Length..];
        }

        return string.Equals(text, RestoreLine, StringComparison.Ordinal);
    }
}
