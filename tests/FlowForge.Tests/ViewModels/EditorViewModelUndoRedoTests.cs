using System.Text.Json;
using Avalonia;
using FlowForge.Core.Execution;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class EditorViewModelUndoRedoTests
{
    private static NodeRegistry CreateRegistry()
    {
        return NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
    }

    [Fact]
    public void AddNode_CanBeUndone()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);

        editor.AddNode("FolderInput", new Point(100, 200), registry);

        editor.Nodes.Should().HaveCount(1);
        editor.UndoRedo.CanUndo.Should().BeTrue();

        editor.Undo();

        editor.Nodes.Should().BeEmpty();
        editor.UndoRedo.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void AddNode_Redo_RestoresNode()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);

        editor.AddNode("FolderInput", new Point(100, 200), registry);
        editor.Undo();
        editor.Redo();

        editor.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveSelectedNodes_CanBeUndone()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);

        editor.AddNode("FolderInput", new Point(100, 200), registry);
        editor.Nodes[0].IsSelected = true;

        editor.RemoveSelectedNodes();

        editor.Nodes.Should().BeEmpty();

        editor.Undo();

        editor.Nodes.Should().HaveCount(1);
        editor.Nodes[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void ClearAll_ClearsUndoRedoHistory()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);

        editor.AddNode("FolderInput", new Point(0, 0), registry);
        editor.AddNode("FolderInput", new Point(100, 100), registry);
        editor.UndoRedo.CanUndo.Should().BeTrue();

        editor.ClearAll();

        editor.UndoRedo.CanUndo.Should().BeFalse();
        editor.UndoRedo.CanRedo.Should().BeFalse();
        editor.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void ConfigChange_CanBeUndone_ViaUndoRedoManager()
    {
        var config = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("/old"),
        };
        JsonElement oldVal = config["path"];
        JsonElement newVal = JsonSerializer.SerializeToElement("/new");

        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, CreateRegistry());

        // Simulate what ConfigFieldViewModel does: mutate config first, then PushExecuted
        config["path"] = newVal;
        var cmd = new ChangeConfigCommand(config, "path", oldVal, newVal, keyExisted: true, "Change path");
        editor.UndoRedo.PushExecuted(cmd);

        config["path"].GetString().Should().Be("/new");

        editor.Undo();
        config["path"].GetString().Should().Be("/old");

        editor.Redo();
        config["path"].GetString().Should().Be("/new");
    }

    [Fact]
    public void ConnectingIntoCycle_IsRefused()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);
        PipelineNodeViewModel a = AddRename(editor, registry);
        PipelineNodeViewModel b = AddRename(editor, registry);
        PipelineNodeViewModel c = AddRename(editor, registry);
        Connect(editor, a, b);
        Connect(editor, b, c);
        int count = editor.Connections.Count;
        string? undo = editor.UndoRedo.UndoDescription;

        Connect(editor, c, a);

        editor.Connections.Should().HaveCount(count, "C → A would close the loop A → B → C → A");
        editor.UndoRedo.CanUndo.Should().BeTrue();
        editor.UndoRedo.UndoDescription.Should().Be(undo);
        editor.PendingConnection.IsVisible.Should().BeFalse("a refused connection still ends the drag");
        editor.Undo();
        editor.Connections.Should().ContainSingle("one undo removes B → C, the last connection made")
            .Which.Source.Should().BeSameAs(a.Output[0]);
    }

    [Fact]
    public void ReverseOfExistingConnection_IsRefused()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);
        PipelineNodeViewModel a = AddRename(editor, registry);
        PipelineNodeViewModel b = AddRename(editor, registry);
        Connect(editor, a, b);
        string? undo = editor.UndoRedo.UndoDescription;

        Connect(editor, b, a);

        editor.Connections.Should().ContainSingle("B → A would close the loop A → B → A")
            .Which.Source.Should().BeSameAs(a.Output[0]);
        editor.UndoRedo.UndoDescription.Should().Be(undo);
        editor.Undo();
        editor.Connections.Should().BeEmpty("one undo removes A → B, the only connection made");
    }

    private static PipelineNodeViewModel AddRename(EditorViewModel editor, NodeRegistry registry)
    {
        editor.AddNode("RenamePattern", new Point(300 * editor.Nodes.Count, 0), registry);
        return editor.Nodes[^1];
    }

    // A drag from one station's output port dropped on another's input port, as the canvas sends it.
    private static void Connect(EditorViewModel editor, PipelineNodeViewModel from, PipelineNodeViewModel to)
    {
        editor.PendingConnection.StartCommand.Execute(from.Output[0]);
        editor.PendingConnection.FinishCommand.Execute(to.Input[0]);
    }
}
