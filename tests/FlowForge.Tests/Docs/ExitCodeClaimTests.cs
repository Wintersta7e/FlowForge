using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using FlowForge.CLI;
using FlowForge.Tests.Cli;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class ExitCodeClaimTests
{
    private const string ReadmePath = "README.md";
    private const string GuidePath = "docs/USER_GUIDE.md";
    private const string CasesPath = "tests/FlowForge.Tests/Cli/CliExitCodeTests.cs";

    private readonly ITestOutputHelper _output;

    public ExitCodeClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Documented_exit_codes_match_the_exit_code_table()
    {
        int[] constants = typeof(CliExitCode)
            .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => f.IsLiteral)
            .Select(f => (int)f.GetRawConstantValue()!)
            .Order()
            .ToArray();
        var mismatches = new List<string>();

        DocumentedCodes readme = ReadmeCodes(File.ReadAllLines(RepoRoot.Combine("README.md")));
        DocumentedCodes guide = GuideCodes(File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md")));
        CompareWithConstants(ReadmePath, "no line contains 'exit codes ('", readme, constants, mismatches);
        CompareWithConstants(GuidePath, "no '### Exit Codes' heading", guide, constants, mismatches);

        var exercised = CliExitCodeTests.Cases.Select(row => (int)row[2]!)
            .Concat(CliExitCodeMappingTests.Cases.Select(row => (int)row[4]!))
            .ToHashSet();
        foreach (int constant in constants.Where(k => !exercised.Contains(k)))
        {
            mismatches.Add($"{CasesPath}:1: exit code {constant} is exercised by no CLI test case");
        }

        ClaimCheck.Report(_output, "exit-codes", readme.Codes.Count + guide.Codes.Count, constants.Length * 2, mismatches);
    }

    private static void CompareWithConstants(
        string path, string missingSection, DocumentedCodes doc, int[] constants, List<string> mismatches)
    {
        if (doc.SectionLine == 0)
        {
            mismatches.Add($"{path}:1: {missingSection}");
            return;
        }

        foreach ((int line, int code) in doc.Codes.Where(c => !constants.Contains(c.Code)))
        {
            mismatches.Add($"{path}:{line}: documented exit code {code} is not a {nameof(CliExitCode)} constant");
        }

        foreach (int constant in constants.Where(k => doc.Codes.TrueForAll(c => c.Code != k)))
        {
            mismatches.Add($"{path}:{doc.SectionLine}: exit code {constant} is not documented");
        }
    }

    // The README states the codes inline: from "exit codes (" through the first ")".
    private static DocumentedCodes ReadmeCodes(string[] lines)
    {
        const string marker = "exit codes (";
        var codes = new List<(int Line, int Code)>();
        int start = Array.FindIndex(lines, l => l.Contains(marker, StringComparison.Ordinal));
        if (start < 0)
        {
            return new DocumentedCodes(0, codes);
        }

        for (int i = start; i < lines.Length; i++)
        {
            string text = i == start
                ? lines[i][(lines[i].IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]
                : lines[i];
            int close = text.IndexOf(')', StringComparison.Ordinal);
            foreach (Match match in BacktickedNumber().Matches(close < 0 ? text : text[..close]))
            {
                codes.Add((i + 1, int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture)));
            }

            if (close >= 0)
            {
                break;
            }
        }

        return new DocumentedCodes(start + 1, codes);
    }

    // The user guide states the codes as table rows under "### Exit Codes", up to the next heading.
    private static DocumentedCodes GuideCodes(string[] lines)
    {
        var codes = new List<(int Line, int Code)>();
        int start = Array.FindIndex(lines, l => string.Equals(l.Trim(), "### Exit Codes", StringComparison.Ordinal));
        if (start < 0)
        {
            return new DocumentedCodes(0, codes);
        }

        for (int i = start + 1; i < lines.Length && !lines[i].StartsWith('#'); i++)
        {
            Match row = TableRowCode().Match(lines[i]);
            if (row.Success)
            {
                codes.Add((i + 1, int.Parse(row.Groups["code"].Value, CultureInfo.InvariantCulture)));
            }
        }

        return new DocumentedCodes(start + 1, codes);
    }

    [GeneratedRegex(@"`(?<code>\d+)`", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BacktickedNumber();

    [GeneratedRegex(@"^\|\s*(?<code>\d+)\s*\|", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TableRowCode();

    // SectionLine is the 1-based line where the codes start, or 0 when the document has no such section.
    private sealed record DocumentedCodes(int SectionLine, List<(int Line, int Code)> Codes);
}
