using FluentAssertions;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;

namespace FlowForge.Tests.Pipeline;

public class PipelineTemplateLibraryTests
{
    [Fact]
    public void Templates_returns_exactly_4_templates()
    {
        PipelineTemplateLibrary.Templates.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("photo-import-by-date")]
    [InlineData("batch-sequential-rename")]
    [InlineData("image-web-export")]
    [InlineData("bulk-image-compress")]
    public void Templates_contains_expected_id(string expectedId)
    {
        PipelineTemplateLibrary.Templates
            .Should().Contain(t => t.Id == expectedId);
    }

    [Fact]
    public void CreateFromTemplate_produces_graph_with_unique_non_empty_node_ids()
    {
        PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate("photo-import-by-date");

        graph.Nodes.Should().NotBeEmpty();
        graph.Nodes.Select(n => n.Id).Should().OnlyContain(id => id != Guid.Empty);
        graph.Nodes.Select(n => n.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void CreateFromTemplate_called_twice_produces_different_node_ids()
    {
        PipelineGraph first = PipelineTemplateLibrary.CreateFromTemplate("batch-sequential-rename");
        PipelineGraph second = PipelineTemplateLibrary.CreateFromTemplate("batch-sequential-rename");

        List<Guid> firstIds = first.Nodes.Select(n => n.Id).ToList();
        List<Guid> secondIds = second.Nodes.Select(n => n.Id).ToList();

        firstIds.Should().NotIntersectWith(secondIds);
    }

    [Fact]
    public void CreateFromTemplate_unknown_id_throws_ArgumentException()
    {
        Action act = () => PipelineTemplateLibrary.CreateFromTemplate("nonexistent-template");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*nonexistent-template*");
    }

    [Theory]
    [InlineData("photo-import-by-date", 4)]
    [InlineData("batch-sequential-rename", 3)]
    [InlineData("image-web-export", 5)]
    [InlineData("bulk-image-compress", 4)]
    public void Template_has_expected_node_count(string templateId, int expectedCount)
    {
        PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(templateId);

        graph.Nodes.Should().HaveCount(expectedCount);
    }

    [Theory]
    [InlineData("photo-import-by-date", 3)]
    [InlineData("batch-sequential-rename", 2)]
    [InlineData("image-web-export", 4)]
    [InlineData("bulk-image-compress", 3)]
    public void Connections_are_linear(string templateId, int expectedConnections)
    {
        PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(templateId);

        graph.Connections.Should().HaveCount(expectedConnections);

        for (int i = 0; i < graph.Connections.Count; i++)
        {
            graph.Connections[i].FromNode.Should().Be(graph.Nodes[i].Id);
            graph.Connections[i].ToNode.Should().Be(graph.Nodes[i + 1].Id);
        }
    }

    [Theory]
    [InlineData("photo-import-by-date", new[] { "FolderInput", "MetadataExtract", "RenamePattern", "FolderOutput" })]
    [InlineData("batch-sequential-rename", new[] { "FolderInput", "RenamePattern", "FolderOutput" })]
    [InlineData("image-web-export", new[] { "FolderInput", "Filter", "ImageResize", "ImageConvert", "FolderOutput" })]
    [InlineData("bulk-image-compress", new[] { "FolderInput", "Filter", "ImageCompress", "FolderOutput" })]
    public void Template_nodes_have_expected_type_keys(string templateId, string[] expectedTypeKeys)
    {
        PipelineGraph graph = PipelineTemplateLibrary.CreateFromTemplate(templateId);

        List<string> actualKeys = graph.Nodes.Select(n => n.TypeKey).ToList();
        actualKeys.Should().Equal(expectedTypeKeys);
    }
}
