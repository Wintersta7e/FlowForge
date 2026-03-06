using FlowForge.UI.UndoRedo;
using FluentAssertions;

namespace FlowForge.Tests.UndoRedo;

public class UndoRedoManagerTests
{
    private sealed class FakeCommand : IUndoableCommand
    {
        public string Description { get; }

        public int ExecuteCount { get; private set; }

        public int UndoCount { get; private set; }

        public FakeCommand(string description = "fake")
        {
            Description = description;
        }

        public void Execute() => ExecuteCount++;

        public void Undo() => UndoCount++;
    }

    [Fact]
    public void Execute_PushesToUndoStack()
    {
        var manager = new UndoRedoManager();
        var cmd = new FakeCommand();

        manager.Execute(cmd);

        manager.CanUndo.Should().BeTrue();
        manager.CanRedo.Should().BeFalse();
        cmd.ExecuteCount.Should().Be(1);
    }

    [Fact]
    public void Undo_RevertsCommand()
    {
        var manager = new UndoRedoManager();
        var cmd = new FakeCommand();

        manager.Execute(cmd);
        manager.Undo();

        cmd.UndoCount.Should().Be(1);
        manager.CanUndo.Should().BeFalse();
        manager.CanRedo.Should().BeTrue();
    }

    [Fact]
    public void Redo_ReExecutesCommand()
    {
        var manager = new UndoRedoManager();
        var cmd = new FakeCommand();

        manager.Execute(cmd);
        manager.Undo();
        manager.Redo();

        cmd.ExecuteCount.Should().Be(2);
        manager.CanUndo.Should().BeTrue();
        manager.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Execute_ClearsRedoStack()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand());
        manager.Undo();
        manager.CanRedo.Should().BeTrue();

        manager.Execute(new FakeCommand("new"));

        manager.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void StackCap_DropsOldestAt25()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand("oldest"));
        for (int i = 0; i < 25; i++)
        {
            manager.Execute(new FakeCommand($"cmd-{i}"));
        }

        for (int i = 0; i < 25; i++)
        {
            manager.CanUndo.Should().BeTrue();
            manager.Undo();
        }

        manager.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsBothStacks()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand());
        manager.Execute(new FakeCommand());
        manager.Undo();
        manager.Clear();

        manager.CanUndo.Should().BeFalse();
        manager.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void UndoDescription_MatchesLastCommand()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand("Add node"));

        manager.UndoDescription.Should().Be("Add node");
    }

    [Fact]
    public void RedoDescription_MatchesLastUndoneCommand()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand("Add node"));
        manager.Undo();

        manager.RedoDescription.Should().Be("Add node");
    }

    [Fact]
    public void Undo_WhenEmpty_DoesNothing()
    {
        var manager = new UndoRedoManager();

        Action act = () => manager.Undo();

        act.Should().NotThrow();
    }

    [Fact]
    public void Redo_WhenEmpty_DoesNothing()
    {
        var manager = new UndoRedoManager();

        Action act = () => manager.Redo();

        act.Should().NotThrow();
    }

    [Fact]
    public void StateChanged_FiresOnExecute()
    {
        var manager = new UndoRedoManager();
        bool fired = false;
        manager.StateChanged += (_, _) => fired = true;

        manager.Execute(new FakeCommand());

        fired.Should().BeTrue();
    }

    [Fact]
    public void StateChanged_FiresOnUndo()
    {
        var manager = new UndoRedoManager();
        manager.Execute(new FakeCommand());
        bool fired = false;
        manager.StateChanged += (_, _) => fired = true;

        manager.Undo();

        fired.Should().BeTrue();
    }

    [Fact]
    public void PushExecuted_AddsToUndoWithoutCallingExecute()
    {
        var manager = new UndoRedoManager();
        var cmd = new FakeCommand("already done");

        manager.PushExecuted(cmd);

        cmd.ExecuteCount.Should().Be(0);
        manager.CanUndo.Should().BeTrue();
        manager.UndoDescription.Should().Be("already done");
    }

    [Fact]
    public void PushExecuted_ClearsRedoStack()
    {
        var manager = new UndoRedoManager();

        manager.Execute(new FakeCommand());
        manager.Undo();
        manager.CanRedo.Should().BeTrue();

        manager.PushExecuted(new FakeCommand("new"));

        manager.CanRedo.Should().BeFalse();
    }
}
