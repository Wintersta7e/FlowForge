using System;
using System.Collections.Generic;

namespace FlowForge.UI.UndoRedo;

public class UndoRedoManager
{
    private const int MaxStackSize = 25;
    private readonly LinkedList<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    public string? UndoDescription => _undoStack.Last?.Value.Description;

    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    public event EventHandler? StateChanged;

    public void Execute(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.AddLast(command);

        if (_undoStack.Count > MaxStackSize)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records a command that has already been executed (e.g., by MVVM binding or Nodify drag).
    /// Does NOT call command.Execute().
    /// </summary>
    public void PushExecuted(IUndoableCommand command)
    {
        _undoStack.AddLast(command);

        if (_undoStack.Count > MaxStackSize)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Replaces the last command on the undo stack if the predicate matches,
    /// otherwise pushes as a new entry. Used to coalesce repeated edits (e.g., keystrokes).
    /// </summary>
    public void PushOrCoalesce(IUndoableCommand command, Func<IUndoableCommand, bool> shouldReplace)
    {
        if (_undoStack.Count > 0 && shouldReplace(_undoStack.Last!.Value))
        {
            _undoStack.RemoveLast();
        }

        _undoStack.AddLast(command);

        if (_undoStack.Count > MaxStackSize)
        {
            _undoStack.RemoveFirst();
        }

        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
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

        if (_undoStack.Count > MaxStackSize)
        {
            _undoStack.RemoveFirst();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
