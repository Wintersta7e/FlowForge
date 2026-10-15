using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.UndoRedo.Commands;
using Microsoft.Extensions.Logging;

namespace FlowForge.UI.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    private readonly ILogger<EditorViewModel> _logger;
    private readonly NodeRegistry _registry;
    private readonly HashSet<PipelineNodeViewModel> _subscribedNodes = new();
    private Dictionary<PipelineNodeViewModel, Point>? _dragStartPositions;
    private string _graphName = "Untitled Pipeline";
    private bool _validitySuspended;

    public ObservableCollection<PipelineNodeViewModel> Nodes { get; } = new();
    public bool HasNodes => Nodes.Count > 0;
    public ObservableCollection<PipelineConnectionViewModel> Connections { get; } = new();
    public PipelinePendingConnectionViewModel PendingConnection { get; }
    public UndoRedoManager UndoRedo { get; } = new();

    /// <summary>One card per library template, shown on the empty canvas.</summary>
    public IReadOnlyList<TemplateCardViewModel> TemplateCards { get; }

    /// <summary>
    /// Raised when any graph content changes (node config edits, structural changes).
    /// </summary>
    public event EventHandler? GraphChanged;

    /// <summary>
    /// Raised to request the view to execute FitToScreen on the NodifyEditor.
    /// </summary>
    public event EventHandler? FitToScreenRequested;

    public void RequestFitToScreen() => FitToScreenRequested?.Invoke(this, EventArgs.Empty);

    private PipelineNodeViewModel? _selectedNode;
    public PipelineNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public EditorViewModel(ILogger<EditorViewModel> logger, NodeRegistry registry)
    {
        _logger = logger;
        _registry = registry;
        PendingConnection = new PipelinePendingConnectionViewModel(this);
        TemplateCards = BuildTemplateCards();
        Nodes.CollectionChanged += OnNodesCollectionChanged;
        Nodes.CollectionChanged += OnNodesHasNodesChanged;
        Nodes.CollectionChanged += OnNodesGraphChanged;
        Connections.CollectionChanged += OnConnectionsGraphChanged;
        UndoRedo.StateChanged += OnUndoRedoStateChanged;
    }

    // Every config edit, and its undo and redo, passes through the history after mutating the config.
    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        foreach (PipelineNodeViewModel node in Nodes)
        {
            node.RefreshConfigPreview();
        }
    }

    // A card takes the colour of its template's first station that is not a source.
    private static List<TemplateCardViewModel> BuildTemplateCards()
    {
        var cards = new List<TemplateCardViewModel>();
        IReadOnlyList<PipelineTemplate> templates = PipelineTemplateLibrary.Templates;
        for (int i = 0; i < templates.Count; i++)
        {
            PipelineTemplate template = templates[i];
            string category = template.Graph.Nodes
                .Select(node => MwOpsMap.Get(node.TypeKey).Category)
                .FirstOrDefault(c => !string.Equals(c, "src", StringComparison.Ordinal)) ?? "src";
            IBrush accent = ThemeHelper.GetBrush(MwOpsMap.KeysFor(category).Neon, "#ff7a1a");
            cards.Add(new TemplateCardViewModel(
                template.Id,
                string.Create(CultureInfo.InvariantCulture, $"TPL.{i + 1:00}"),
                template.ShortName,
                template.Description,
                template.Graph.Nodes.Count,
                accent));
        }

        return cards;
    }

    private void OnNodesHasNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNodes));
    }

    private void OnNodesGraphChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ShowValidity();
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnConnectionsGraphChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ShowValidity();
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseGraphChanged()
    {
        ShowValidity();
        GraphChanged?.Invoke(this, EventArgs.Empty);
    }

    // Puts what the graph check reports on the nodes and connections it names. Issues naming a
    // connection go to every pipe between its two nodes; issues naming the whole graph are not shown.
    private void ShowValidity()
    {
        if (_validitySuspended)
        {
            return;
        }

        IReadOnlyList<GraphIssue> issues = PipelineGraphValidator.Validate(BuildGraph(), _registry).Issues;
        foreach (PipelineNodeViewModel node in Nodes)
        {
            node.ValidationMessage = JoinMessages(issues.Where(i => i.FromNode is null && i.NodeId == node.Id));
        }

        foreach (PipelineConnectionViewModel connection in Connections)
        {
            connection.ValidationMessage = JoinMessages(issues.Where(i =>
                i.FromNode == connection.Source.Node.Id && i.ToNode == connection.Target.Node.Id));
        }
    }

    private static string? JoinMessages(IEnumerable<GraphIssue> issues)
    {
        string joined = string.Join('\n', issues.Select(i => i.Message));
        return joined.Length == 0 ? null : joined;
    }

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

                // A node put back by undo keeps the selection it was removed with, but that flag
                // was set while it was unsubscribed, so the inspector would not follow it.
                if (node.IsSelected)
                {
                    SelectedNode = node;
                }
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
        if (string.Equals(e.PropertyName, nameof(PipelineNodeViewModel.IsSelected), StringComparison.Ordinal) && sender is PipelineNodeViewModel node)
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
        _validitySuspended = true;
        try
        {
            UnsubscribeAllNodes();
            Nodes.Clear();
            Connections.Clear();
            SelectedNode = null;
            _graphName = "Untitled Pipeline";
            UndoRedo.Clear();
        }
        finally
        {
            _validitySuspended = false;
        }

        ShowValidity();
    }

    // The graph is checked once, after every node and connection is in place.
    public int LoadGraph(PipelineGraph graph, NodeRegistry registry)
    {
        _validitySuspended = true;
        int droppedConnections;
        try
        {
            droppedConnections = LoadNodesAndConnections(graph, registry);
        }
        finally
        {
            _validitySuspended = false;
        }

        ShowValidity();
        return droppedConnections;
    }

    private int LoadNodesAndConnections(PipelineGraph graph, NodeRegistry registry)
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
                // A copy: the run configures nodes off the UI thread while the inspector can
                // still edit the node's own dictionary.
                Config = new Dictionary<string, JsonElement>(nodeVm.Config, StringComparer.Ordinal)
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
        var selected = Nodes.Where(n => n.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var attachedConnections = Connections
            .Where(c => selected.Contains(c.Source.Node) || selected.Contains(c.Target.Node))
            .ToList();

        UndoRedo.Execute(new RemoveNodesCommand(Nodes, Connections, selected, attachedConnections));
        SelectedNode = null;
    }

    // Alt+click on a port: every pipe on it goes, as one undo step.
    [RelayCommand]
    private void DisconnectConnector(PipelineConnectorViewModel connector)
    {
        var attached = Connections.Where(c => c.Source == connector || c.Target == connector).ToList();
        if (attached.Count == 0)
        {
            return;
        }

        UndoRedo.Execute(new DisconnectCommand(Connections, attached));
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
