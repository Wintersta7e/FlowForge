using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;

namespace FlowForge.UI.ViewModels;

public partial class EditorViewModel : ViewModelBase
{
    public ObservableCollection<PipelineNodeViewModel> Nodes { get; } = new();
    public ObservableCollection<PipelineConnectionViewModel> Connections { get; } = new();
    public PipelinePendingConnectionViewModel PendingConnection { get; }

    private PipelineNodeViewModel? _selectedNode;
    public PipelineNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public EditorViewModel()
    {
        PendingConnection = new PipelinePendingConnectionViewModel(this);
        Nodes.CollectionChanged += OnNodesCollectionChanged;
    }

    private void UnsubscribeAllNodes()
    {
        foreach (PipelineNodeViewModel node in Nodes)
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PipelineNodeViewModel node in e.NewItems)
            {
                node.PropertyChanged += OnNodePropertyChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PipelineNodeViewModel node in e.OldItems)
            {
                node.PropertyChanged -= OnNodePropertyChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
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
    }

    public int LoadGraph(PipelineGraph graph, NodeRegistry registry)
    {
        UnsubscribeAllNodes();
        Nodes.Clear();
        Connections.Clear();

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
                droppedConnections++;
                continue;
            }

            PipelineConnectorViewModel? sourceConnector = fromNode.Output.Count > 0 ? fromNode.Output[0] : null;
            PipelineConnectorViewModel? targetConnector = toNode.Input.Count > 0 ? toNode.Input[0] : null;

            if (sourceConnector is null || targetConnector is null)
            {
                droppedConnections++;
                continue;
            }

            sourceConnector.IsConnected = true;
            targetConnector.IsConnected = true;

            Connections.Add(new PipelineConnectionViewModel(sourceConnector, targetConnector));
        }

        return droppedConnections;
    }

    public PipelineGraph BuildGraph()
    {
        PipelineGraph graph = new()
        {
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

    public void AddNode(string typeKey, Point position, NodeRegistry registry)
    {
        NodeDefinition definition = new()
        {
            TypeKey = typeKey,
            Position = new CanvasPosition(position.X, position.Y)
        };

        PipelineNodeViewModel nodeVm = new(definition, registry);
        Nodes.Add(nodeVm);
    }

    public void RemoveSelectedNodes()
    {
        List<PipelineNodeViewModel> selected = Nodes.Where(n => n.IsSelected).ToList();

        foreach (PipelineNodeViewModel node in selected)
        {
            // Remove connections attached to this node
            List<PipelineConnectionViewModel> attachedConnections = Connections
                .Where(c => c.Source.Node == node || c.Target.Node == node)
                .ToList();

            foreach (PipelineConnectionViewModel conn in attachedConnections)
            {
                conn.Source.IsConnected = false;
                conn.Target.IsConnected = false;
                Connections.Remove(conn);
            }

            Nodes.Remove(node);
        }

        SelectedNode = null;
    }
}
