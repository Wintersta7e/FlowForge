using System.Collections.Generic;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class CompositeCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    public string Description { get; }

    public CompositeCommand(IReadOnlyList<IUndoableCommand> commands, string description)
    {
        _commands = commands;
        Description = description;
    }

    public void Execute()
    {
        for (int i = 0; i < _commands.Count; i++)
        {
            _commands[i].Execute();
        }
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
        {
            _commands[i].Undo();
        }
    }
}
