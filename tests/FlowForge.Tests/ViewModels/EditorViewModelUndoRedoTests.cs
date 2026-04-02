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
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

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
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

        editor.AddNode("FolderInput", new Point(100, 200), registry);
        editor.Undo();
        editor.Redo();

        editor.Nodes.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveSelectedNodes_CanBeUndone()
    {
        NodeRegistry registry = CreateRegistry();
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

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
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

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

        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);

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
}
