using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.UndoRedo;

public class CommandTests
{
    private static PipelineNodeViewModel CreateTransformNode()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        var def = new NodeDefinition
        {
            TypeKey = "RenamePattern",
            Position = new CanvasPosition(0, 0),
        };
        return new PipelineNodeViewModel(def, registry);
    }

    [Fact]
    public void AddNodeCommand_Execute_AddsNode()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        PipelineNodeViewModel node = CreateTransformNode();
        var cmd = new AddNodeCommand(nodes, node);

        cmd.Execute();

        nodes.Should().Contain(node);
    }

    [Fact]
    public void AddNodeCommand_Undo_RemovesNode()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        PipelineNodeViewModel node = CreateTransformNode();
        var cmd = new AddNodeCommand(nodes, node);

        cmd.Execute();
        cmd.Undo();

        nodes.Should().NotContain(node);
    }

    [Fact]
    public void AddNodeCommand_Description_IncludesNodeTitle()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        PipelineNodeViewModel node = CreateTransformNode();
        var cmd = new AddNodeCommand(nodes, node);

        cmd.Description.Should().StartWith("Add ");
    }

    [Fact]
    public void RemoveNodesCommand_Undo_RestoresNodesAndConnections()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        nodes.Add(node1);
        nodes.Add(node2);

        PipelineConnectorViewModel source = node1.Output[0];
        PipelineConnectorViewModel target = node2.Input[0];
        source.IsConnected = true;
        target.IsConnected = true;
        var conn = new PipelineConnectionViewModel(source, target);
        connections.Add(conn);

        var removedNodes = new List<PipelineNodeViewModel> { node1 };
        var removedConnections = new List<PipelineConnectionViewModel> { conn };
        var cmd = new RemoveNodesCommand(nodes, connections, removedNodes, removedConnections);

        cmd.Execute();
        nodes.Should().NotContain(node1);
        connections.Should().NotContain(conn);

        cmd.Undo();
        nodes.Should().Contain(node1);
        connections.Should().Contain(conn);
        source.IsConnected.Should().BeTrue();
        target.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void RemoveNodesCommand_SingleNode_DescriptionIncludesTitle()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node = CreateTransformNode();
        nodes.Add(node);

        var cmd = new RemoveNodesCommand(
            nodes,
            connections,
            new List<PipelineNodeViewModel> { node },
            new List<PipelineConnectionViewModel>());

        cmd.Description.Should().StartWith("Remove ");
        cmd.Description.Should().Contain(node.Title);
    }

    [Fact]
    public void RemoveNodesCommand_MultipleNodes_DescriptionShowsCount()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        nodes.Add(node1);
        nodes.Add(node2);

        var cmd = new RemoveNodesCommand(
            nodes,
            connections,
            new List<PipelineNodeViewModel> { node1, node2 },
            new List<PipelineConnectionViewModel>());

        cmd.Description.Should().Be("Remove 2 nodes");
    }

    [Fact]
    public void MoveNodeCommand_Undo_RestoresPosition()
    {
        PipelineNodeViewModel node = CreateTransformNode();
        var oldPos = new Point(10, 20);
        var newPos = new Point(100, 200);
        node.Location = oldPos;
        var cmd = new MoveNodeCommand(node, oldPos, newPos);

        cmd.Execute();
        node.Location.Should().Be(newPos);

        cmd.Undo();
        node.Location.Should().Be(oldPos);
    }

    [Fact]
    public void MoveNodeCommand_Description_IsMoveNode()
    {
        PipelineNodeViewModel node = CreateTransformNode();
        var cmd = new MoveNodeCommand(node, new Point(0, 0), new Point(1, 1));

        cmd.Description.Should().Be("Move node");
    }

    [Fact]
    public void ConnectCommand_Undo_RemovesConnection()
    {
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        PipelineConnectorViewModel source = node1.Output[0];
        PipelineConnectorViewModel target = node2.Input[0];
        var conn = new PipelineConnectionViewModel(source, target);

        var cmd = new ConnectCommand(connections, conn);

        cmd.Execute();
        connections.Should().Contain(conn);
        source.IsConnected.Should().BeTrue();
        target.IsConnected.Should().BeTrue();

        cmd.Undo();
        connections.Should().NotContain(conn);
        source.IsConnected.Should().BeFalse();
        target.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void ConnectCommand_Description_IsConnectNodes()
    {
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        var conn = new PipelineConnectionViewModel(node1.Output[0], node2.Input[0]);
        var cmd = new ConnectCommand(connections, conn);

        cmd.Description.Should().Be("Connect nodes");
    }

    [Fact]
    public void DisconnectCommand_Undo_RestoresConnection()
    {
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        PipelineConnectorViewModel source = node1.Output[0];
        PipelineConnectorViewModel target = node2.Input[0];
        source.IsConnected = true;
        target.IsConnected = true;
        var conn = new PipelineConnectionViewModel(source, target);
        connections.Add(conn);

        var cmd = new DisconnectCommand(connections, conn);

        cmd.Execute();
        connections.Should().NotContain(conn);
        source.IsConnected.Should().BeFalse();
        target.IsConnected.Should().BeFalse();

        cmd.Undo();
        connections.Should().Contain(conn);
        source.IsConnected.Should().BeTrue();
        target.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void DisconnectCommand_Description_IsDisconnectNodes()
    {
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node1 = CreateTransformNode();
        PipelineNodeViewModel node2 = CreateTransformNode();
        var conn = new PipelineConnectionViewModel(node1.Output[0], node2.Input[0]);
        connections.Add(conn);
        var cmd = new DisconnectCommand(connections, conn);

        cmd.Description.Should().Be("Disconnect nodes");
    }

    [Fact]
    public void ChangeConfigCommand_Undo_RestoresOldValue()
    {
        var config = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("/old/path"),
        };
        JsonElement oldValue = config["path"];
        JsonElement newValue = JsonSerializer.SerializeToElement("/new/path");

        var cmd = new ChangeConfigCommand(config, "path", oldValue, newValue, "Change path");

        cmd.Execute();
        config["path"].GetString().Should().Be("/new/path");

        cmd.Undo();
        config["path"].GetString().Should().Be("/old/path");
    }

    [Fact]
    public void ChangeConfigCommand_Description_MatchesProvided()
    {
        var config = new Dictionary<string, JsonElement>
        {
            ["key"] = JsonSerializer.SerializeToElement("val"),
        };
        JsonElement old = config["key"];
        JsonElement @new = JsonSerializer.SerializeToElement("val2");

        var cmd = new ChangeConfigCommand(config, "key", old, @new, "Update key");

        cmd.Description.Should().Be("Update key");
    }
}
