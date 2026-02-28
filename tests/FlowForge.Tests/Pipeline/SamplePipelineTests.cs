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
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
