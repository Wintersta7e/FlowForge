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

    [Fact]
    public void StateChanged_FiresOnRedo()
    {
        var manager = new UndoRedoManager();
        manager.Execute(new FakeCommand());
        manager.Undo();
        bool fired = false;
        manager.StateChanged += (_, _) => fired = true;

        manager.Redo();

        fired.Should().BeTrue();
    }

    [Fact]
    public void StateChanged_FiresOnClear()
    {
        var manager = new UndoRedoManager();
        manager.Execute(new FakeCommand());
        bool fired = false;
        manager.StateChanged += (_, _) => fired = true;

        manager.Clear();

        fired.Should().BeTrue();
    }

    [Fact]
    public void StateChanged_FiresOnPushExecuted()
    {
        var manager = new UndoRedoManager();
        bool fired = false;
        manager.StateChanged += (_, _) => fired = true;

        manager.PushExecuted(new FakeCommand());

        fired.Should().BeTrue();
    }

    [Fact]
    public void Redo_CapsCombinedStackAt25()
    {
        var manager = new UndoRedoManager();

        for (int i = 0; i < 25; i++)
        {
            manager.Execute(new FakeCommand($"cmd-{i}"));
        }

        manager.Undo();
        manager.Redo();

        // After redo, should still only have 25 items max
        int undoCount = 0;
        while (manager.CanUndo)
        {
            manager.Undo();
            undoCount++;
        }

        undoCount.Should().BeLessThanOrEqualTo(25);
    }

    [Fact]
    public void PushOrCoalesce_ReplacesMatchingLast()
    {
        var manager = new UndoRedoManager();
        var cmd1 = new FakeCommand("edit-1");
        var cmd2 = new FakeCommand("edit-2");

        manager.PushExecuted(cmd1);
        manager.PushOrCoalesce(cmd2, prev => prev.Description.StartsWith("edit", StringComparison.Ordinal));

        // Should have replaced, not added
        int count = 0;
        while (manager.CanUndo)
        {
            manager.Undo();
            count++;
        }
        count.Should().Be(1);
    }

    [Fact]
    public void PushOrCoalesce_PushesWhenNoMatch()
    {
        var manager = new UndoRedoManager();
        var cmd1 = new FakeCommand("move");
        var cmd2 = new FakeCommand("edit");

        manager.PushExecuted(cmd1);
        manager.PushOrCoalesce(cmd2, prev => prev.Description.StartsWith("edit", StringComparison.Ordinal));

        // cmd1 doesn't match predicate, so both should be on stack
        int count = 0;
        while (manager.CanUndo)
        {
            manager.Undo();
            count++;
        }
        count.Should().Be(2);
    }

    [Fact]
    public void PushOrCoalesce_ClearsRedoStack()
    {
        var manager = new UndoRedoManager();
        manager.Execute(new FakeCommand());
        manager.Undo();
        manager.CanRedo.Should().BeTrue();

        manager.PushOrCoalesce(new FakeCommand("new"), _ => false);

        manager.CanRedo.Should().BeFalse();
    }

    [Fact]
    public void Undo_WhenCommandThrows_PreservesStackState()
    {
        var manager = new UndoRedoManager();
        var good = new FakeCommand("good");
        var bad = new ThrowingCommand();

        manager.Execute(good);
        manager.Execute(bad);

        // bad.Undo() throws — command should stay on undo stack
        Action act = () => manager.Undo();
        act.Should().Throw<InvalidOperationException>();

        manager.CanUndo.Should().BeTrue("command stays on stack after failed undo");
    }

    private sealed class ThrowingCommand : IUndoableCommand
    {
        public string Description => "throws";
        public void Execute() { }
        public void Undo() => throw new InvalidOperationException("undo failed");
    }
}
