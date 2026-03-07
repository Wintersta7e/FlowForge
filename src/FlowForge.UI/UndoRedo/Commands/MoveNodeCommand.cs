using Avalonia;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class MoveNodeCommand : IUndoableCommand
{
    private readonly PipelineNodeViewModel _node;
    private readonly Point _oldPosition;
    private readonly Point _newPosition;

    public string Description => "Move node";

    public MoveNodeCommand(PipelineNodeViewModel node, Point oldPosition, Point newPosition)
    {
        _node = node;
        _oldPosition = oldPosition;
        _newPosition = newPosition;
    }

    public void Execute() => _node.Location = _newPosition;

    public void Undo() => _node.Location = _oldPosition;
}
