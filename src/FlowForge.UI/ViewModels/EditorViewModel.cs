using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.UndoRedo.Commands;
using Microsoft.Extensions.Logging;

namespace FlowForge.UI.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    private readonly ILogger<EditorViewModel> _logger;
    private readonly HashSet<PipelineNodeViewModel> _subscribedNodes = new();
    private Dictionary<PipelineNodeViewModel, Point>? _dragStartPositions;
    private string _graphName = "Untitled Pipeline";

    public ObservableCollection<PipelineNodeViewModel> Nodes { get; } = new();
    public bool HasNodes => Nodes.Count > 0;
    public ObservableCollection<PipelineConnectionViewModel> Connections { get; } = new();
    public PipelinePendingConnectionViewModel PendingConnection { get; }
    public UndoRedoManager UndoRedo { get; } = new();

    /// <summary>
    /// Raised when any graph content changes (node config edits, structural changes).
    /// </summary>
    public event EventHandler? GraphChanged;

    /// <summary>
    /// Raised to request the view to execute FitToScreen on the NodifyEditor.
    /// </summary>
    public event Action? FitToScreenRequested;

    public void RequestFitToScreen() => FitToScreenRequested?.Invoke();

    private PipelineNodeViewModel? _selectedNode;
    public PipelineNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public EditorViewModel(ILogger<EditorViewModel> logger)
    {
        _logger = logger;
        PendingConnection = new PipelinePendingConnectionViewModel(this);
        Nodes.CollectionChanged += OnNodesCollectionChanged;
        Nodes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNodes));
        Nodes.CollectionChanged += (_, _) => GraphChanged?.Invoke(this, EventArgs.Empty);
        Connections.CollectionChanged += (_, _) => GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseGraphChanged() => GraphChanged?.Invoke(this, EventArgs.Empty);

    private void UnsubscribeAllNodes()
    {
        foreach (PipelineNodeViewModel node in _subscribedNodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }

        _subscribedNodes.Clear();
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PipelineNodeViewModel node in e.NewItems)
            {
                node.PropertyChanged += OnNodePropertyChanged;
                _subscribedNodes.Add(node);
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PipelineNodeViewModel node in e.OldItems)
            {
                node.PropertyChanged -= OnNodePropertyChanged;
                _subscribedNodes.Remove(node);
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            // Nodes collection is already empty at this point;
            // use _subscribedNodes to unsubscribe from the old nodes
            UnsubscribeAllNodes();
            SelectedNode = null;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PipelineNodeViewModel.IsSelected) && sender is PipelineNodeViewModel node)
        {
            if (node.IsSelected)
            {
                SelectedNode = node;
            }
            else if (SelectedNode == node)
            {
                SelectedNode = null;
            }
        }
    }

    public void ClearAll()
    {
        UnsubscribeAllNodes();
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;
        _graphName = "Untitled Pipeline";
        UndoRedo.Clear();
    }

    public int LoadGraph(PipelineGraph graph, NodeRegistry registry)
    {
        UnsubscribeAllNodes();
        Nodes.Clear();
        Connections.Clear();

        _graphName = graph.Name;

        int droppedConnections = 0;

        // Create node VMs
        Dictionary<Guid, PipelineNodeViewModel> nodeMap = new();
        foreach (NodeDefinition nodeDef in graph.Nodes)
        {
            PipelineNodeViewModel nodeVm = new(nodeDef, registry);
            nodeMap[nodeDef.Id] = nodeVm;
            Nodes.Add(nodeVm);
        }

        // Create connection VMs
        foreach (Connection conn in graph.Connections)
        {
            if (!nodeMap.TryGetValue(conn.FromNode, out PipelineNodeViewModel? fromNode) ||
                !nodeMap.TryGetValue(conn.ToNode, out PipelineNodeViewModel? toNode))
            {
                _logger.LogWarning("LoadGraph: dropping connection — node not found (From={FromNode}, To={ToNode})",
                    conn.FromNode, conn.ToNode);
                droppedConnections++;
                continue;
            }

            PipelineConnectorViewModel? sourceConnector = fromNode.Output.Count > 0 ? fromNode.Output[0] : null;
            PipelineConnectorViewModel? targetConnector = toNode.Input.Count > 0 ? toNode.Input[0] : null;

            if (sourceConnector is null || targetConnector is null)
            {
                _logger.LogWarning("LoadGraph: dropping connection — missing connector (From={FromType}, To={ToType})",
                    fromNode.TypeKey, toNode.TypeKey);
                droppedConnections++;
                continue;
            }

            sourceConnector.IsConnected = true;
            targetConnector.IsConnected = true;

            Connections.Add(new PipelineConnectionViewModel(sourceConnector, targetConnector));
        }

        UndoRedo.Clear();
        return droppedConnections;
    }

    public PipelineGraph BuildGraph()
    {
        PipelineGraph graph = new()
        {
            Name = _graphName,
            Nodes = new List<NodeDefinition>(),
            Connections = new List<Connection>()
        };

        foreach (PipelineNodeViewModel nodeVm in Nodes)
        {
            NodeDefinition nodeDef = new()
            {
                Id = nodeVm.Id,
                TypeKey = nodeVm.TypeKey,
                Position = new CanvasPosition(nodeVm.Location.X, nodeVm.Location.Y),
                Config = nodeVm.Config
            };
            graph.Nodes.Add(nodeDef);
        }

        foreach (PipelineConnectionViewModel connVm in Connections)
        {
            Connection conn = new()
            {
                FromNode = connVm.Source.Node.Id,
                FromPin = "out",
                ToNode = connVm.Target.Node.Id,
                ToPin = "in"
            };
            graph.Connections.Add(conn);
        }

        return graph;
    }

    public void Undo() => UndoRedo.Undo();
    public void Redo() => UndoRedo.Redo();

    public void AddNode(string typeKey, Point position, NodeRegistry registry)
    {
        NodeDefinition definition = new()
        {
            TypeKey = typeKey,
            Position = new CanvasPosition(position.X, position.Y)
        };

        PipelineNodeViewModel nodeVm = new(definition, registry);
        UndoRedo.Execute(new AddNodeCommand(Nodes, nodeVm));
    }

    public void RemoveSelectedNodes()
    {
        List<PipelineNodeViewModel> selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        List<PipelineConnectionViewModel> attachedConnections = Connections
            .Where(c => selected.Contains(c.Source.Node) || selected.Contains(c.Target.Node))
            .ToList();

        UndoRedo.Execute(new RemoveNodesCommand(Nodes, Connections, selected, attachedConnections));
        SelectedNode = null;
    }

    [RelayCommand]
    private void ItemsDragStarted()
    {
        _dragStartPositions = Nodes
            .Where(n => n.IsSelected)
            .ToDictionary(n => n, n => n.Location);
    }

    [RelayCommand]
    private void ItemsDragCompleted()
    {
        if (_dragStartPositions is null)
        {
            return;
        }

        var moves = new List<IUndoableCommand>();
        foreach (KeyValuePair<PipelineNodeViewModel, Point> kvp in _dragStartPositions)
        {
            PipelineNodeViewModel node = kvp.Key;
            Point oldPos = kvp.Value;
            Point newPos = node.Location;

            if (oldPos != newPos)
            {
                moves.Add(new MoveNodeCommand(node, oldPos, newPos));
            }
        }

        if (moves.Count == 1)
        {
            UndoRedo.PushExecuted(moves[0]);
        }
        else if (moves.Count > 1)
        {
            UndoRedo.PushExecuted(new CompositeCommand(moves, $"Move {moves.Count} nodes"));
        }

        _dragStartPositions = null;
    }
}
