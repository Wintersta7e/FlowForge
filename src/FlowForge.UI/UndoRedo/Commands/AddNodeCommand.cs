using System.Collections.ObjectModel;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class AddNodeCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineNodeViewModel> _nodes;
    private readonly PipelineNodeViewModel _node;

    public string Description { get; }

    public AddNodeCommand(ObservableCollection<PipelineNodeViewModel> nodes, PipelineNodeViewModel node)
    {
        _nodes = nodes;
        _node = node;
        Description = $"Add {node.Title}";
    }

    public void Execute() => _nodes.Add(_node);

    public void Undo() => _nodes.Remove(_node);
}
