using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public partial class CliUsageClaimTests
{
    private const string ProgramPrefix = "flowforge ";
    private const string ProjectMarker = "src/FlowForge.CLI --";
    private const string GuidePath = "docs/USER_GUIDE.md";
    private static readonly string[] DocumentPaths = ["README.md", GuidePath];
    private static readonly string[] HelpOptions = ["--help", "--version"];

    private readonly ITestOutputHelper _output;

    public CliUsageClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Documented_cli_commands_and_options_exist()
    {
        CliHelp root = await HelpAsync(["--help"]);
        CliHelp run = await HelpAsync(["run", "--help"]);
        var optionsByCommand = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [string.Empty] = root.Options,
            ["run"] = run.Options,
        };

        var documented = DocumentPaths.SelectMany(DocumentedLines).ToList();
        var mismatches = new List<string>();
        if (!documented.Exists(d => d.Program is not null))
        {
            mismatches.Add($"{GuidePath}:1: no fenced line in README.md or {GuidePath} starts with '{ProgramPrefix}'");
        }

        foreach (DocumentedLine line in documented)
        {
            string at = $"{line.Path}:{line.Line}";
            if (line.Program is not null && !string.Equals(line.Program, root.Program, StringComparison.Ordinal))
            {
                mismatches.Add($"{at}: program '{line.Program}' is not the help's Usage name '{root.Program}'");
            }

            if (line.Command is not null && !root.Commands.Contains(line.Command, StringComparer.Ordinal))
            {
                mismatches.Add($"{at}: unknown command '{line.Command}'");
                continue;
            }

            HashSet<string> known = optionsByCommand.GetValueOrDefault(line.Command ?? string.Empty) ?? [];
            foreach (string option in line.Options.Where(o => !known.Contains(o)))
            {
                mismatches.Add($"{at}: option '{option}' is not an option of {(line.Command is null ? "the root command" : $"'{line.Command}'")}");
            }
        }

        var runLines = documented.Where(d => string.Equals(d.Command, "run", StringComparison.Ordinal)).ToList();
        string runAt = runLines.Count > 0 ? $"{runLines[0].Path}:{runLines[0].Line}" : $"{GuidePath}:1";
        string[] runOptions = run.Options.Where(o => !HelpOptions.Contains(o, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        foreach (string option in runOptions.Where(o => !runLines.Exists(d => d.Options.Contains(o, StringComparer.Ordinal))))
        {
            mismatches.Add($"{runAt}: run option '{option}' is documented nowhere");
        }

        ClaimCheck.Report(_output, "cli-usage", runOptions.Length, 5, mismatches);
    }

    // Lines inside ``` fences that start "flowforge " or run the CLI project through "dotnet run".
    private static IEnumerable<DocumentedLine> DocumentedLines(string path)
    {
        string[] lines = File.ReadAllLines(RepoRoot.Combine(path.Split('/')));
        bool inFence = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            int marker = text.IndexOf(ProjectMarker, StringComparison.Ordinal);
            string? program = null;
            string[] tokens;
            if (inFence && text.StartsWith(ProgramPrefix, StringComparison.Ordinal))
            {
                tokens = Tokens(text);
                program = tokens[0];
                tokens = tokens[1..];
            }
            else if (inFence && marker >= 0)
            {
                tokens = Tokens(text[(marker + ProjectMarker.Length)..]);
            }
            else
            {
                continue;
            }

            string? command = tokens.Length > 0 && !tokens[0].StartsWith('-') ? tokens[0] : null;
            var options = tokens.Select(t => OptionToken().Match(t)).Where(m => m.Success && m.Index == 0).Select(m => m.Value).ToList();
            yield return new DocumentedLine(path, i + 1, program, command, options);
        }
    }

    // Spawns the built CLI with the given help arguments and reads its Usage name, commands and options.
    private static async Task<CliHelp> HelpAsync(string[] args)
    {
        CliRun run = await CliProcess.RunAsync(args, AppContext.BaseDirectory, CancellationToken.None);

        string nl = Environment.NewLine;
        run.ExitCode.Should().Be(0, "{0}", $"stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}");
        string[] lines = run.StdOut.ReplaceLineEndings("\n").Split('\n');
        string program = SectionLines(lines, "Usage:").Select(l => Tokens(l)[0]).FirstOrDefault() ?? string.Empty;
        var commands = SectionLines(lines, "Commands:").Select(l => Tokens(l)[0]).ToList();
        var options = SectionLines(lines, "Options:")
            .SelectMany(l => OptionToken().Matches(l).Select(m => m.Value))
            .ToHashSet(StringComparer.Ordinal);
        return new CliHelp(program, commands, options);
    }

    // The non-blank lines under a help heading such as "Options:", up to the next blank line.
    private static IEnumerable<string> SectionLines(string[] lines, string heading)
    {
        int start = Array.FindIndex(lines, l => string.Equals(l.Trim(), heading, StringComparison.Ordinal));
        for (int i = start + 1; start >= 0 && i < lines.Length && lines[i].Trim().Length > 0; i++)
        {
            yield return lines[i];
        }
    }

    private static string[] Tokens(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [GeneratedRegex("--[a-z][a-z-]*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OptionToken();

    private sealed record CliHelp(string Program, IReadOnlyList<string> Commands, HashSet<string> Options);

    // Program is null for a "dotnet run --project" line; Command is null when the first argument is an option.
    private sealed record DocumentedLine(string Path, int Line, string? Program, string? Command, IReadOnlyList<string> Options);
}
