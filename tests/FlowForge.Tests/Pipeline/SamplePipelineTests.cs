using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.Tests.Docs;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Pipeline;

public partial class SamplePipelineTests
{
    private const string SamplesReadme = "samples/README.md";
    private const string NodesPrefix = "- **Nodes:** ";
    private const string ChainSeparator = " → ";

    private readonly ITestOutputHelper _output;

    public SamplePipelineTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("photo-import-by-date")]
    [InlineData("batch-sequential-rename")]
    [InlineData("image-web-export")]
    [InlineData("bulk-image-compress")]
    public async Task Template_serializes_and_deserializes_cleanly(string templateId)
    {
        PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(templateId);

        // Set portable placeholder paths
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (string.Equals(node.TypeKey, "FolderInput", StringComparison.Ordinal))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement("./input");
            }
            else if (string.Equals(node.TypeKey, "FolderOutput", StringComparison.Ordinal))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement("./output");
            }
        }

        string tempFile = Path.Combine(Path.GetTempPath(), $"{templateId}.ffpipe");
        try
        {
            await PipelineSerializer.SaveAsync(graph, tempFile);
            PipelineGraph loaded = await PipelineSerializer.LoadAsync(tempFile);

            loaded.Nodes.Should().HaveCount(graph.Nodes.Count);
            loaded.Connections.Should().HaveCount(graph.Connections.Count);

            // Verify TypeKey and Config are preserved after round-trip
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                loaded.Nodes[i].TypeKey.Should().Be(graph.Nodes[i].TypeKey,
                    $"node at index {i} should preserve TypeKey after round-trip");
                loaded.Nodes[i].Config.Should().NotBeEmpty(
                    $"node at index {i} ({graph.Nodes[i].TypeKey}) should preserve Config after round-trip");
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Every_sample_loads_and_configures_cleanly()
    {
        string samplesDir = RepoRoot.Combine("samples");
        string[] files = Directory.Exists(samplesDir)
            ? Directory.GetFiles(samplesDir, "*.ffpipe").OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : [];
        _output.WriteLine($"[samples] scanned={files.Length}");
        files.Should().NotBeEmpty("the samples directory must hold at least one .ffpipe file");

        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        List<string> failures = [];
        Dictionary<string, (int Line, string[] Chain)> documented =
            DocumentedChains(File.ReadAllLines(RepoRoot.Combine("samples", "README.md")));
        int chainMismatches = 0;
        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            PipelineGraph graph;
            try
            {
                graph = await PipelineSerializer.LoadAsync(file);
            }
            catch (PipelineLoadException ex)
            {
                failures.Add($"{name}: {ex.Message}");
                continue;
            }

            if (!string.Equals(graph.Version, KnownVersions.Current, StringComparison.Ordinal))
            {
                failures.Add($"{name}: version {graph.Version}");
            }

            failures.AddRange(graph.LoadNotices.Select(n => $"{name}: notice {n.Code}"));

            string[] chain = ClaimCheck.TopologicalTypeKeys(graph)
                .Select(k => registry.IsRegistered(k) ? registry.GetDisplayName(k) : k)
                .ToArray();
            if (!documented.TryGetValue(name, out (int Line, string[] Chain) doc))
            {
                failures.Add($"{SamplesReadme}: {name}: no heading");
                chainMismatches++;
            }
            else if (!doc.Chain.SequenceEqual(chain, StringComparer.Ordinal))
            {
                failures.Add($"{SamplesReadme}:{doc.Line}: {name}: documented {string.Join(ChainSeparator, doc.Chain)}, graph {string.Join(ChainSeparator, chain)}");
                chainMismatches++;
            }

            if (graph.Nodes.Count == 0)
            {
                failures.Add($"{name}: no nodes");
            }

            if (graph.Connections.Count == 0)
            {
                failures.Add($"{name}: no connections");
            }

            foreach (NodeDefinition node in graph.Nodes)
            {
                string? error = ConfigureNode(registry, node);
                if (error != null)
                {
                    failures.Add($"{name}: node {node.Id} ({node.TypeKey}): {error}");
                }
            }
        }

        var enumerated = files.Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        foreach ((string name, (int line, string[] _)) in documented)
        {
            if (!enumerated.Contains(name))
            {
                failures.Add($"{SamplesReadme}:{line}: {name}: documented, but no such sample file");
                chainMismatches++;
            }
        }

        _output.WriteLine($"[claims] sample-chains scanned={documented.Count} mismatches={chainMismatches}");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void Every_template_configures_cleanly()
    {
        string[] ids = PipelineTemplateLibrary.Templates.Select(t => t.Id).ToArray();
        _output.WriteLine($"[templates] scanned={ids.Length}");
        ids.Should().NotBeEmpty("the template library must hold at least one template");

        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "input");
        Directory.CreateDirectory(inputPath);

        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        List<string> failures = [];
        foreach (string id in ids)
        {
            PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(id);
            foreach (NodeDefinition node in graph.Nodes)
            {
                if (string.Equals(node.TypeKey, "FolderInput", StringComparison.Ordinal))
                {
                    node.Config["path"] = JsonSerializer.SerializeToElement(inputPath);
                }
                else if (string.Equals(node.TypeKey, "FolderOutput", StringComparison.Ordinal))
                {
                    node.Config["path"] = JsonSerializer.SerializeToElement(dir.OutputPath);
                }

                string? error = ConfigureNode(registry, node);
                if (error != null)
                {
                    failures.Add($"{id}: node {node.Id} ({node.TypeKey}): {error}");
                }
            }
        }

        failures.Should().BeEmpty();
    }

    /// <summary>
    /// Creates the node through the registry, which runs its Configure; returns the
    /// failure text, or null when the node configured cleanly.
    /// </summary>
    private static string? ConfigureNode(NodeRegistry registry, NodeDefinition node)
    {
        if (!registry.IsRegistered(node.TypeKey))
        {
            return "type is not registered";
        }

        try
        {
            switch (registry.GetCategoryForTypeKey(node.TypeKey))
            {
                case NodeCategory.Source:
                    _ = registry.GetSource(node);
                    break;
                case NodeCategory.Transform:
                    _ = registry.GetTransform(node);
                    break;
                case NodeCategory.Output:
                    _ = registry.GetOutput(node);
                    break;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Reads each <c>### &lt;file&gt;.ffpipe</c> heading of the samples README and the <c>- **Nodes:**</c> chain
    /// below it, keyed by file name, with the 1-based line of the chain (of the heading when it has none).
    /// </summary>
    private static Dictionary<string, (int Line, string[] Chain)> DocumentedChains(string[] lines)
    {
        var chains = new Dictionary<string, (int Line, string[] Chain)>(StringComparer.Ordinal);
        for (int i = 0; i < lines.Length; i++)
        {
            Match heading = SampleHeading().Match(lines[i]);
            if (!heading.Success)
            {
                continue;
            }

            (int Line, string[] Chain) entry = (i + 1, []);
            for (int j = i + 1; j < lines.Length && !lines[j].StartsWith('#'); j++)
            {
                if (lines[j].StartsWith(NodesPrefix, StringComparison.Ordinal))
                {
                    entry = (j + 1, lines[j][NodesPrefix.Length..].Trim().Split(ChainSeparator));
                    break;
                }
            }

            chains.TryAdd(heading.Groups["file"].Value, entry);
        }

        return chains;
    }

    [GeneratedRegex(@"^### (?<file>\S+\.ffpipe)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SampleHeading();
}
