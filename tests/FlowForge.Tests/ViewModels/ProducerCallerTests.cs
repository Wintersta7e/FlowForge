using System.Text.RegularExpressions;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.ViewModels;

/// <summary>
/// Each member the editor gained to show or drive a state is read somewhere in the UI project:
/// a binding path in a view, or code that reads or calls it. The member's own declaration,
/// assignments to it, comments and plain string text are not readers.
/// </summary>
public sealed partial class ProducerCallerTests
{
    private static readonly string[] Producers =
    [
        "SaveAsCommand",
        "DisconnectCommand",
        "IsDirty",
        "PhaseLabel",
        "DiscoveredFiles",
        "CanUndo",
        "CanRedo",
        "UndoDescription",
        "RedoDescription",
        "NodeLog",
        "Timestamp",
        "OpenFolderAsync",
    ];

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private readonly ITestOutputHelper _output;

    public ProducerCallerTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EveryListedProducer_HasAProductionReference()
    {
        string root = RepoRoot.Combine("src", "FlowForge.UI");
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToList();
        Dictionary<string, List<string>> references = Producers.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        foreach (string file in files)
        {
            bool view = file.EndsWith(".axaml", StringComparison.Ordinal);
            string site = Path.GetRelativePath(RepoRoot.FullPath, file).Replace('\\', '/');
            string[] lines = await File.ReadAllLinesAsync(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string name in Producers)
                {
                    int count = view ? BindingReferences(lines[i], name) : CodeReferences(CodeOn(lines[i]), name);
                    references[name].AddRange(Enumerable.Repeat($"{site}:{i + 1}", count));
                }
            }
        }

        _output.WriteLine($"[producers] files={files.Count} producers={Producers.Length}");
        foreach ((string name, List<string> sites) in references)
        {
            _output.WriteLine($"{name} {sites.Count} {string.Join(' ', sites)}");
        }

        files.Should().NotBeEmpty("a scan of zero files proves nothing");
        references.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToList()
            .Should().BeEmpty("every listed producer is read by a view binding or by code in the UI project");
    }

    // Uses of the name inside the line's binding markup.
    private static int BindingReferences(string line, string name) =>
        Binding().Matches(line).Sum(binding => Word(name).Count(binding.Value));

    // Uses of the name in code, less the one a declaration on the line names and each assignment to it.
    private static int CodeReferences(string code, string name)
    {
        Match declaration = Declaration(name).Match(code);
        return Word(name).Matches(code).Count(use =>
            !(declaration.Success && use.Index == declaration.Groups["name"].Index)
            && !Assignment().IsMatch(code[(use.Index + use.Length)..]));
    }

    // The code on a line, with char literals and plain string text emptied and a trailing comment cut.
    // A line holding an interpolated string is kept whole, since the holes in it are code.
    private static string CodeOn(string line)
    {
        if (Interpolated().IsMatch(line))
        {
            return line;
        }

        string code = StringOrChar().Replace(line, "\"\"");
        int comment = code.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? code : code[..comment];
    }

    private static Regex Word(string name) => new($@"\b{Regex.Escape(name)}\b", RegexOptions.None, MatchTimeout);

    // A type, member or constructor declaration whose declared name is this one; a statement that
    // starts with a keyword (return, await, new …) does not declare it.
    private static Regex Declaration(string name) => new(
        @"^\s*(?:\[[^\]]*\]\s*)*"
        + @"(?:(?:public|internal|protected|private|static|sealed|partial|async|override|virtual|abstract|readonly|required|event|const)\s+)*"
        + @"(?:class|interface|record|struct|enum|(?!(?:return|await|new|throw|yield|else|case|is|as|out|ref|in|nameof)\b)[\w.]+(?:<[^;=()]*>)?(?:\[\])?\??)"
        + $@"\s+(?<name>{Regex.Escape(name)})\b",
        RegexOptions.None,
        MatchTimeout);

    [GeneratedRegex(@"\{(?:Binding|CompiledBinding|ReflectionBinding)\b[^}]*\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Binding();

    [GeneratedRegex(@"^\s*(?:=(?![=>])|\+=|-=|\?\?=)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Assignment();

    [GeneratedRegex(@"\$@?""|@\$""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Interpolated();

    [GeneratedRegex(@"@""(?:[^""]|"""")*""|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)'", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex StringOrChar();
}
