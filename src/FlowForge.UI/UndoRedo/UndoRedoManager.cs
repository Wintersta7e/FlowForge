using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowForge.UI.UndoRedo;

public class UndoRedoManager
{
    private const int MaxStackSize = 25;
    private readonly LinkedList<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    // The command on top of the undo stack at the last save (null: the stack was empty), and
    // whether the history can still prove a return to that state.
    private IUndoableCommand? _savedTop;
    private bool _savePointReachable = true;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDescription => _undoStack.Last?.Value.Description;

    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>True while the edits since the last <see cref="MarkSavePoint"/> are provably undone.</summary>
    public bool IsAtSavePoint => _savePointReachable && ReferenceEquals(_undoStack.Last?.Value, _savedTop);

    public event EventHandler? StateChanged;

    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        Push(command);
    }

    /// <summary>
    /// Records a command that has already been executed (e.g., by MVVM binding or Nodify drag).
    /// Does NOT call command.Execute().
    /// </summary>
    public void PushExecuted(IUndoableCommand command)
    {
        Push(command);
    }

    /// <summary>
    /// Replaces the last command with what <paramref name="merge"/> makes of it, so repeated edits
    /// (e.g., keystrokes) undo as one; pushes <paramref name="command"/> as a new entry when merge
    /// returns null or the last command is the saved one, so one undo still returns to the save.
    /// </summary>
    public void PushOrCoalesce(IUndoableCommand command, Func<IUndoableCommand, IUndoableCommand?> merge)
    {
        IUndoableCommand? top = _undoStack.Last?.Value;
        IUndoableCommand? merged = top is null || ReferenceEquals(top, _savedTop) ? null : merge(top);
        if (merged is not null)
        {
            _undoStack.RemoveLast();
        }

        Push(merged ?? command);
    }

    public void Undo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        IUndoableCommand command = _undoStack.Last!.Value;
        command.Undo();
        _undoStack.RemoveLast();
        _redoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        IUndoableCommand command = _redoStack.Peek();
        command.Execute();
        _redoStack.Pop();
        _undoStack.AddLast(command);
        TrimToCap();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _savePointReachable = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records the current state as saved.</summary>
    public void MarkSavePoint()
    {
        _savedTop = _undoStack.Last?.Value;
        _savePointReachable = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Push(IUndoableCommand command)
    {
        // The redo stack is about to be dropped; a saved state waiting there is gone with it.
        if (_redoStack.Any(redo => ReferenceEquals(redo, _savedTop)))
        {
            _savePointReachable = false;
        }

        _undoStack.AddLast(command);
        TrimToCap();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // An evicted command stays applied but can no longer be undone. Any eviction gives up the
    // save point: it drops the saved command, one below it, or the empty start undo returned to.
    private void TrimToCap()
    {
        if (_undoStack.Count > MaxStackSize)
        {
            _undoStack.RemoveFirst();
            _savePointReachable = false;
        }
    }
}
