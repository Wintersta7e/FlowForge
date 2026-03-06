using System.Collections.Generic;
using System.Collections.ObjectModel;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class RemoveNodesCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineNodeViewModel> _nodes;
    private readonly ObservableCollection<PipelineConnectionViewModel> _connections;
    private readonly List<PipelineNodeViewModel> _removedNodes;
    private readonly List<PipelineConnectionViewModel> _removedConnections;

    public string Description { get; }

    public RemoveNodesCommand(
        ObservableCollection<PipelineNodeViewModel> nodes,
        ObservableCollection<PipelineConnectionViewModel> connections,
        List<PipelineNodeViewModel> removedNodes,
        List<PipelineConnectionViewModel> removedConnections)
    {
        _nodes = nodes;
        _connections = connections;
        _removedNodes = removedNodes;
        _removedConnections = removedConnections;
        Description = removedNodes.Count == 1
            ? $"Remove {removedNodes[0].Title}"
            : $"Remove {removedNodes.Count} nodes";
    }

    public void Execute()
    {
        foreach (PipelineConnectionViewModel conn in _removedConnections)
        {
            conn.Source.IsConnected = false;
            conn.Target.IsConnected = false;
            _connections.Remove(conn);
        }

        foreach (PipelineNodeViewModel node in _removedNodes)
        {
            _nodes.Remove(node);
        }
    }

    public void Undo()
    {
        foreach (PipelineNodeViewModel node in _removedNodes)
        {
            _nodes.Add(node);
        }

        foreach (PipelineConnectionViewModel conn in _removedConnections)
        {
            conn.Source.IsConnected = true;
            conn.Target.IsConnected = true;
            _connections.Add(conn);
        }
    }
}
