using FluentAssertions;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;

namespace FlowForge.Tests.Pipeline;

public class PipelineSerializerTests
{
    [Fact]
    public async Task SaveAndLoad_roundtrip_preserves_graph()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "test.ffpipe");

        var graph = new PipelineGraph
        {
            Name = "Round Trip Test",
        };
        graph.Nodes.Add(new NodeDefinition { TypeKey = "FolderInput" });
        graph.Nodes.Add(new NodeDefinition { TypeKey = "RenamePattern" });
        graph.Connections.Add(new Connection
        {
            FromNode = graph.Nodes[0].Id,
            ToNode = graph.Nodes[1].Id
        });

        await PipelineSerializer.SaveAsync(graph, filePath);
        PipelineGraph loaded = await PipelineSerializer.LoadAsync(filePath);

        loaded.Name.Should().Be("Round Trip Test");
        loaded.Nodes.Should().HaveCount(2);
        loaded.Nodes[0].TypeKey.Should().Be("FolderInput");
        loaded.Nodes[1].TypeKey.Should().Be("RenamePattern");
        loaded.Connections.Should().HaveCount(1);
        loaded.Connections[0].FromNode.Should().Be(graph.Nodes[0].Id);
        loaded.Connections[0].ToNode.Should().Be(graph.Nodes[1].Id);
    }

    [Fact]
    public async Task Load_missing_file_throws_FileNotFoundException()
    {
        Func<Task> act = () => PipelineSerializer.LoadAsync("/nonexistent/path/test.ffpipe");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Load_invalid_json_throws_PipelineLoadException()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "bad.ffpipe");
        await File.WriteAllTextAsync(filePath, "this is not valid json {{{");

        Func<Task> act = () => PipelineSerializer.LoadAsync(filePath);
        await act.Should().ThrowAsync<PipelineLoadException>();
    }

    [Fact]
    public async Task Save_wrong_extension_throws_ArgumentException()
    {
        var graph = new PipelineGraph();
        Func<Task> act = () => PipelineSerializer.SaveAsync(graph, "/tmp/test.json");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Save_creates_valid_json_file()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "output.ffpipe");

        var graph = new PipelineGraph { Name = "JSON Test" };
        await PipelineSerializer.SaveAsync(graph, filePath);

        string content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("\"name\":");
        content.Should().Contain("JSON Test");
    }
}
