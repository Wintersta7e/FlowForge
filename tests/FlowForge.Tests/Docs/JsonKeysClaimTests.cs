using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

public class JsonKeysClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public JsonKeysClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Documented_json_keys_match_the_cli_output()
    {
        var mismatches = new List<string>();
        (int fenceLine, IReadOnlyList<string> sample, HashSet<string>? documented) =
            DocumentedKeys(File.ReadAllLines(RepoRoot.Combine("docs", "USER_GUIDE.md")), mismatches);

        HashSet<string> written = await WrittenKeysAsync();

        if (documented is not null)
        {
            foreach (string key in documented.Where(k => !written.Contains(k)).Order(StringComparer.Ordinal))
            {
                mismatches.Add($"{GuidePath}:{KeyLine(fenceLine, sample, key)}: key '{key}' is documented but the CLI does not write it");
            }

            foreach (string key in written.Where(k => !documented.Contains(k)).Order(StringComparer.Ordinal))
            {
                mismatches.Add($"{GuidePath}:{fenceLine}: the CLI writes key '{key}' but the sample omits it");
            }
        }

        ClaimCheck.Report(_output, "json-keys", written.Count, 7, mismatches);
    }

    // The first ```json fence under "### JSON Output": its line, its body, and its top-level keys (null when absent or unparseable).
    private static (int FenceLine, IReadOnlyList<string> Sample, HashSet<string>? Keys) DocumentedKeys(string[] lines, List<string> mismatches)
    {
        int heading = Array.FindIndex(lines, l => string.Equals(l.Trim(), "### JSON Output", StringComparison.Ordinal));
        (int StartLine, IReadOnlyList<string> Body)? fence = heading < 0
            ? null
            : ClaimCheck.Section(lines.Skip(heading).ToList(), "```json", "```");
        if (fence is null)
        {
            mismatches.Add($"{GuidePath}:{heading + 1}: no ```json sample under '### JSON Output'");
            return (heading + 1, [], null);
        }

        int fenceLine = heading + fence.Value.StartLine;
        try
        {
            using var doc = JsonDocument.Parse(string.Join('\n', fence.Value.Body));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                mismatches.Add($"{GuidePath}:{fenceLine}: the JSON sample is not an object");
                return (fenceLine, fence.Value.Body, null);
            }

            return (fenceLine, fence.Value.Body, doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal));
        }
        catch (JsonException ex)
        {
            mismatches.Add($"{GuidePath}:{fenceLine}: the JSON sample does not parse: {ex.Message}");
            return (fenceLine, fence.Value.Body, null);
        }
    }

    // Runs the built CLI with --format json over a one-file dry run and returns the top-level keys it writes.
    private static async Task<HashSet<string>> WrittenKeysAsync()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        string output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(input, "a.txt"), "a");

        string pipeline = Path.Combine(dir.Path, "keys.ffpipe");
        PipelineGraph graph = PipelineBuilder.New("CLI JSON keys")
            .AddSource("FolderInput", new { path = input })
            .AddOutput("FolderOutput", new { path = output, mode = "copy" })
            .Build();
        await PipelineSerializer.SaveAsync(graph, pipeline, CancellationToken.None);

        CliRun run = await CliProcess.RunAsync(["run", pipeline, "--dry-run", "--format", "json"], dir.Path, CancellationToken.None);

        string nl = Environment.NewLine;
        run.ExitCode.Should().Be(0, "{0}", $"stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}");
        using var doc = JsonDocument.Parse(run.StdOut);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    // The document line that names the key, or the fence line when no sample line does.
    private static int KeyLine(int fenceLine, IReadOnlyList<string> sample, string key)
    {
        for (int i = 0; i < sample.Count; i++)
        {
            if (sample[i].Contains($"\"{key}\"", StringComparison.Ordinal))
            {
                return fenceLine + 1 + i;
            }
        }

        return fenceLine;
    }
}
