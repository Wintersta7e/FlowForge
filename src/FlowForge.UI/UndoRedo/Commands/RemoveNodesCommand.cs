using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class RemoveNodesCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineNodeViewModel> _nodes;
    private readonly ObservableCollection<PipelineConnectionViewModel> _connections;
    private readonly List<PipelineNodeViewModel> _removedNodes;
    private readonly List<PipelineConnectionViewModel> _removedConnections;
    private readonly Dictionary<PipelineNodeViewModel, bool> _selectionState;

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
        _selectionState = removedNodes.ToDictionary(n => n, n => n.IsSelected);
        Description = removedNodes.Count == 1
            ? $"Remove {removedNodes[0].Title}"
            : $"Remove {removedNodes.Count} nodes";
    }

    public void Execute()
    {
        foreach (PipelineConnectionViewModel conn in _removedConnections)
        {
            _connections.Remove(conn);
        }

        // Recalculate IsConnected for all affected connectors after all removals
        foreach (PipelineConnectionViewModel conn in _removedConnections)
        {
            conn.Source.IsConnected = _connections.Any(c =>
                c.Source == conn.Source || c.Target == conn.Source);
            conn.Target.IsConnected = _connections.Any(c =>
                c.Source == conn.Target || c.Target == conn.Target);
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
            node.IsSelected = _selectionState.GetValueOrDefault(node);
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
