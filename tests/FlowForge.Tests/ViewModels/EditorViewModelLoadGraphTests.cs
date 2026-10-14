using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class EditorViewModelLoadGraphTests
{
    private static PipelineGraph CreateChain()
    {
        PipelineGraph graph = PipelineBuilder
            .New("Round Trip")
            .AddSource("FolderInput", new { path = Path.GetTempPath(), recursive = true, filter = "*.jpg" })
            .AddTransform("RenamePattern", new { pattern = "{counter:000}_{name}{ext}", startIndex = 3 })
            .AddOutput("FolderOutput", new { path = Path.Combine(Path.GetTempPath(), "out"), mode = "copy" })
            .Build();

        for (int i = 0; i < graph.Nodes.Count; i++)
        {
            graph.Nodes[i].Position = new CanvasPosition(120 + (i * 260), 80 + (i * 40));
        }

        return graph;
    }

    [Fact]
    public void LoadGraph_then_BuildGraph_round_trips_nodes_and_connections()
    {
        PipelineGraph graph = CreateChain();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

        int dropped = editor.LoadGraph(graph, NodeRegistry.CreateDefault(NullLoggerFactory.Instance));
        PipelineGraph rebuilt = editor.BuildGraph();

        dropped.Should().Be(0);
        rebuilt.Nodes.Select(n => (n.Id, n.TypeKey, n.Position, Config: JsonSerializer.Serialize(n.Config)))
            .Should().Equal(graph.Nodes.Select(n => (n.Id, n.TypeKey, n.Position, Config: JsonSerializer.Serialize(n.Config))));
        rebuilt.Connections.Select(c => (c.FromNode, c.ToNode))
            .Should().Equal(graph.Connections.Select(c => (c.FromNode, c.ToNode)));
    }

    [Fact]
    public void LoadGraph_drops_a_connection_to_a_missing_node()
    {
        PipelineGraph graph = CreateChain();
        graph.Connections.Add(new Connection { FromNode = graph.Nodes[0].Id, ToNode = Guid.NewGuid() });
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

        int dropped = editor.LoadGraph(graph, NodeRegistry.CreateDefault(NullLoggerFactory.Instance));

        dropped.Should().Be(1);
        editor.Connections.Should().HaveCount(2);
    }
}
