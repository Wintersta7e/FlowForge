using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo;
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

        var cmd = new ChangeConfigCommand(config, "path", oldValue, newValue, keyExisted: true, "Change path");

        cmd.Execute();
        config["path"].GetString().Should().Be("/new/path");

        cmd.Undo();
        config["path"].GetString().Should().Be("/old/path");
    }

    [Fact]
    public void ChangeConfigCommand_Undo_RemovesKeyWhenNotPreviouslyExisting()
    {
        var config = new Dictionary<string, JsonElement>();
        JsonElement newValue = JsonSerializer.SerializeToElement("added");

        var cmd = new ChangeConfigCommand(config, "newKey", default, newValue, keyExisted: false, "Add newKey");

        cmd.Execute();
        config.Should().ContainKey("newKey");
        config["newKey"].GetString().Should().Be("added");

        cmd.Undo();
        config.Should().NotContainKey("newKey");
    }

    [Fact]
    public void ChangeConfigCommand_ConfigKey_ExposedForCoalescing()
    {
        var config = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("/old"),
        };
        JsonElement oldValue = config["path"];
        JsonElement newValue = JsonSerializer.SerializeToElement("/new");

        var cmd = new ChangeConfigCommand(config, "path", oldValue, newValue, keyExisted: true, "Change path");

        cmd.ConfigKey.Should().Be("path");
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

        var cmd = new ChangeConfigCommand(config, "key", old, @new, keyExisted: true, "Update key");

        cmd.Description.Should().Be("Update key");
    }

    [Fact]
    public void CompositeCommand_ExecutesAndUndoesInOrder()
    {
        var config = new Dictionary<string, JsonElement>
        {
            ["a"] = JsonSerializer.SerializeToElement("1"),
            ["b"] = JsonSerializer.SerializeToElement("2"),
        };

        JsonElement oldA = config["a"];
        JsonElement newA = JsonSerializer.SerializeToElement("10");
        JsonElement oldB = config["b"];
        JsonElement newB = JsonSerializer.SerializeToElement("20");

        var commands = new List<IUndoableCommand>
        {
            new ChangeConfigCommand(config, "a", oldA, newA, keyExisted: true, "Change a"),
            new ChangeConfigCommand(config, "b", oldB, newB, keyExisted: true, "Change b"),
        };

        var composite = new CompositeCommand(commands, "Batch change");

        composite.Execute();
        config["a"].GetString().Should().Be("10");
        config["b"].GetString().Should().Be("20");

        composite.Undo();
        config["a"].GetString().Should().Be("1");
        config["b"].GetString().Should().Be("2");

        composite.Description.Should().Be("Batch change");
    }

    [Fact]
    public void ConnectCommand_Undo_WithMultipleConnections_PreservesIsConnected()
    {
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel nodeA = CreateTransformNode();
        PipelineNodeViewModel nodeB = CreateTransformNode();
        PipelineNodeViewModel nodeC = CreateTransformNode();

        PipelineConnectorViewModel sourceA = nodeA.Output[0];
        PipelineConnectorViewModel targetB = nodeB.Input[0];
        PipelineConnectorViewModel targetC = nodeC.Input[0];

        // Connect A->B first
        var conn1 = new PipelineConnectionViewModel(sourceA, targetB);
        var cmd1 = new ConnectCommand(connections, conn1);
        cmd1.Execute();

        // Connect A->C second
        var conn2 = new PipelineConnectionViewModel(sourceA, targetC);
        var cmd2 = new ConnectCommand(connections, conn2);
        cmd2.Execute();

        sourceA.IsConnected.Should().BeTrue();

        // Undo A->C — A should still be connected (A->B remains)
        cmd2.Undo();
        sourceA.IsConnected.Should().BeTrue("A->B connection still exists");
        targetC.IsConnected.Should().BeFalse("C has no remaining connections");
    }

    [Fact]
    public void RemoveNodesCommand_Execute_MultiConnection_RecalculatesIsConnectedCorrectly()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel nodeA = CreateTransformNode();
        PipelineNodeViewModel nodeB = CreateTransformNode();
        PipelineNodeViewModel nodeC = CreateTransformNode();
        nodes.Add(nodeA);
        nodes.Add(nodeB);
        nodes.Add(nodeC);

        // A->B and A->C (A's output has two connections)
        PipelineConnectorViewModel sourceA = nodeA.Output[0];
        PipelineConnectorViewModel targetB = nodeB.Input[0];
        PipelineConnectorViewModel targetC = nodeC.Input[0];
        sourceA.IsConnected = true;
        targetB.IsConnected = true;
        targetC.IsConnected = true;
        var conn1 = new PipelineConnectionViewModel(sourceA, targetB);
        var conn2 = new PipelineConnectionViewModel(sourceA, targetC);
        connections.Add(conn1);
        connections.Add(conn2);

        // Remove both B and C — both connections are removed
        var removedNodes = new List<PipelineNodeViewModel> { nodeB, nodeC };
        var removedConns = new List<PipelineConnectionViewModel> { conn1, conn2 };
        var cmd = new RemoveNodesCommand(nodes, connections, removedNodes, removedConns);

        cmd.Execute();

        // A's output should be false because both connections were removed
        sourceA.IsConnected.Should().BeFalse("all connections from A were removed");
        connections.Should().BeEmpty();
    }

    [Fact]
    public void RemoveNodesCommand_Undo_RestoresSelectionState()
    {
        var nodes = new ObservableCollection<PipelineNodeViewModel>();
        var connections = new ObservableCollection<PipelineConnectionViewModel>();
        PipelineNodeViewModel node = CreateTransformNode();
        node.IsSelected = true;
        nodes.Add(node);

        var cmd = new RemoveNodesCommand(
            nodes, connections,
            new List<PipelineNodeViewModel> { node },
            new List<PipelineConnectionViewModel>());

        cmd.Execute();
        nodes.Should().BeEmpty();

        cmd.Undo();
        node.IsSelected.Should().BeTrue("selection state should be restored from before deletion");
    }
}
