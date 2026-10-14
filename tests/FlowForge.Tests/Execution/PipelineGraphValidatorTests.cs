using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Execution;

public class PipelineGraphValidatorTests
{
    [Fact]
    public void Valid_chain_has_no_issues()
    {
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { })
            .AddTransform("RenamePattern", new { pattern = "{name}{ext}" })
            .AddOutput("FolderOutput", new { })
            .Build();

        GraphValidationResult result = Validate(graph);

        result.Issues.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_graph_is_one_graph_level_refusal()
    {
        GraphValidationResult result = Validate(new PipelineGraph());

        GraphIssue issue = result.Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be(GraphIssueKind.NoNodes);
        issue.Severity.Should().Be(GraphIssueSeverity.Refusal);
        issue.NodeId.Should().BeNull();
        issue.TypeKey.Should().BeNull();
        issue.FromNode.Should().BeNull();
        issue.ToNode.Should().BeNull();
        issue.Message.Should().Be("Pipeline has no nodes.");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unknown_endpoint_names_the_side_and_the_missing_id()
    {
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { })
            .AddOutput("FolderOutput", new { })
            .Build();
        Guid outputId = graph.Nodes[1].Id;
        var missing = Guid.NewGuid();
        graph.Connections.Add(new Connection { FromNode = outputId, ToNode = missing });

        GraphValidationResult result = Validate(graph);

        GraphIssue issue = result.Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be(GraphIssueKind.UnknownTargetNode);
        issue.Severity.Should().Be(GraphIssueSeverity.Refusal);
        issue.ToNode.Should().Be(missing);
        issue.FromNode.Should().Be(outputId);
        issue.NodeId.Should().BeNull();
        issue.Message.Should().Be($"Connection references unknown target node: {missing}");
    }

    [Fact]
    public void Duplicate_connection_is_a_collapsed_warning()
    {
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { })
            .AddOutput("FolderOutput", new { })
            .Build();
        Guid inputId = graph.Nodes[0].Id;
        Guid outputId = graph.Nodes[1].Id;
        graph.Connections.Add(new Connection { FromNode = inputId, ToNode = outputId });

        GraphValidationResult result = Validate(graph);

        result.IsValid.Should().BeTrue();
        result.Refusals.Should().BeEmpty();
        GraphIssue warning = result.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be(GraphIssueKind.DuplicateConnection);
        warning.FromNode.Should().Be(inputId);
        warning.ToNode.Should().Be(outputId);
        warning.Message.Should().Contain("collapsed");
    }

    [Fact]
    public void Inert_node_is_a_per_node_refusal_naming_type_and_id()
    {
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { })
            .AddTransform("RenamePattern", new { pattern = "{name}{ext}" })
            .AddOutput("FolderOutput", new { })
            .Build();
        NodeDefinition stray = PipelineBuilder.Node("RenameAddAffix", new { prefix = "x_" });
        graph.Nodes.Add(stray);

        GraphValidationResult result = Validate(graph);

        GraphIssue refusal = result.Refusals.Should().ContainSingle().Subject;
        refusal.Kind.Should().Be(GraphIssueKind.InertNode);
        refusal.NodeId.Should().Be(stray.Id);
        refusal.TypeKey.Should().Be("RenameAddAffix");
        refusal.Message.Should().Be($"Node 'RenameAddAffix' ({stray.Id}) is not on a path from a source to an output.");
    }

    [Fact]
    public void Cycle_is_reported_before_structural_checks()
    {
        var source = new NodeDefinition { TypeKey = "FolderInput" };
        var rename = new NodeDefinition { TypeKey = "RenamePattern" };
        var graph = new PipelineGraph
        {
            Nodes = { source, rename },
            Connections =
            {
                new Connection { FromNode = source.Id, ToNode = rename.Id },
                new Connection { FromNode = rename.Id, ToNode = source.Id },
            },
        };

        GraphValidationResult result = Validate(graph);

        result.Issues.Should().NotContain(i => i.Kind == GraphIssueKind.ConnectionIntoSource);
        GraphIssue issue = result.Issues.Should().ContainSingle().Subject;
        issue.Kind.Should().Be(GraphIssueKind.Cycle);
        issue.Severity.Should().Be(GraphIssueSeverity.Refusal);
        issue.NodeId.Should().BeNull();
        issue.FromNode.Should().BeNull();
        issue.ToNode.Should().BeNull();
    }

    private static GraphValidationResult Validate(PipelineGraph graph) =>
        PipelineGraphValidator.Validate(graph, NodeRegistry.CreateDefault(NullLoggerFactory.Instance));
}
