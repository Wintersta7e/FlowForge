using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FluentAssertions;

namespace FlowForge.Tests.Pipeline;

public class SamplePipelineTests
{
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
            if (node.TypeKey == "FolderInput")
            {
                node.Config["path"] = JsonSerializer.SerializeToElement("./input");
            }
            else if (node.TypeKey == "FolderOutput")
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

    [Theory]
    [InlineData("samples/photo-import-by-date.ffpipe")]
    [InlineData("samples/batch-sequential-rename.ffpipe")]
    [InlineData("samples/image-web-export.ffpipe")]
    [InlineData("samples/bulk-image-compress.ffpipe")]
    public async Task Sample_ffpipe_files_load_successfully(string relativePath)
    {
        // Find the samples directory relative to the test assembly
        string basePath = AppContext.BaseDirectory;
        string samplePath = Path.GetFullPath(Path.Combine(basePath, "..", "..", "..", "..", "..", relativePath));

        if (!File.Exists(samplePath))
        {
            // Skip if running in CI where samples may not be present
            return;
        }

        PipelineGraph graph = await PipelineSerializer.LoadAsync(samplePath);

        graph.Nodes.Should().NotBeEmpty();
        graph.Connections.Should().NotBeEmpty();
        graph.Nodes.Should().AllSatisfy(n => n.TypeKey.Should().NotBeNullOrWhiteSpace());
    }
}
