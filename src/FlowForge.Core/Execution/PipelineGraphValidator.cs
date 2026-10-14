using FlowForge.Core.Pipeline;

namespace FlowForge.Core.Execution;

/// <summary>
/// Checks a pipeline graph in a fixed order: node types and connection endpoints, cycles, repeated
/// connections, connection direction by node category, then nodes off every source-to-output path.
/// Every offender of a step is reported; the first step that refuses ends the walk. A repeated
/// connection is only a warning: it is collapsed to one.
/// </summary>
public static class PipelineGraphValidator
{
    public static GraphValidationResult Validate(PipelineGraph graph, NodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(registry);

        var issues = new List<GraphIssue>();
        if (graph.Nodes.Count == 0)
        {
            issues.Add(GraphRefusal(GraphIssueKind.NoNodes, "Pipeline has no nodes."));
            return new GraphValidationResult(issues, Array.Empty<Connection>(), Array.Empty<Guid>());
        }

        AddUnknownReferences(graph, registry, issues);
        if (issues.Count > 0)
        {
            return new GraphValidationResult(issues, Array.Empty<Connection>(), Array.Empty<Guid>());
        }

        var duplicates = new List<Connection>();
        List<Connection> connections = Collapse(graph.Connections, duplicates);
        List<Guid> order = TopologicalSort(graph);
        if (order.Count != graph.Nodes.Count)
        {
            issues.Add(GraphRefusal(GraphIssueKind.Cycle, "Pipeline graph contains a cycle."));
            return new GraphValidationResult(issues, connections, Array.Empty<Guid>());
        }

        var nodes = graph.Nodes.ToDictionary(n => n.Id);
        foreach (Connection duplicate in duplicates)
        {
            NodeDefinition from = nodes[duplicate.FromNode];
            NodeDefinition to = nodes[duplicate.ToNode];
            issues.Add(new GraphIssue(
                GraphIssueKind.DuplicateConnection,
                GraphIssueSeverity.Warning,
                $"Duplicate connection from '{from.TypeKey}' ({from.Id}) to '{to.TypeKey}' ({to.Id}) collapsed to one.",
                null,
                null,
                from.Id,
                to.Id));
        }

        var categories = graph.Nodes.ToDictionary(n => n.Id, n => registry.GetCategoryForTypeKey(n.TypeKey));
        if (!AddCategoryRefusals(connections, nodes, categories, issues))
        {
            AddInertNodes(graph.Nodes, connections, categories, issues);
        }

        return new GraphValidationResult(issues, connections, order);
    }

    private static void AddUnknownReferences(PipelineGraph graph, NodeRegistry registry, List<GraphIssue> issues)
    {
        foreach (NodeDefinition node in graph.Nodes.Where(n => !registry.IsRegistered(n.TypeKey)))
        {
            issues.Add(NodeRefusal(GraphIssueKind.UnknownNodeType, $"Unknown node type: '{node.TypeKey}'", node));
        }

        var nodeIds = graph.Nodes.Select(n => n.Id).ToHashSet();
        foreach (Connection conn in graph.Connections)
        {
            if (!nodeIds.Contains(conn.FromNode))
            {
                issues.Add(new GraphIssue(
                    GraphIssueKind.UnknownSourceNode,
                    GraphIssueSeverity.Refusal,
                    $"Connection references unknown source node: {conn.FromNode}",
                    null,
                    null,
                    conn.FromNode,
                    conn.ToNode));
            }
            if (!nodeIds.Contains(conn.ToNode))
            {
                issues.Add(new GraphIssue(
                    GraphIssueKind.UnknownTargetNode,
                    GraphIssueSeverity.Refusal,
                    $"Connection references unknown target node: {conn.ToNode}",
                    null,
                    null,
                    conn.FromNode,
                    conn.ToNode));
            }
        }
    }

    /// <summary>Keeps the first occurrence of each (from, to) pair; the first repeat of each pair goes to <paramref name="duplicates"/>.</summary>
    private static List<Connection> Collapse(IEnumerable<Connection> connections, List<Connection> duplicates)
    {
        var seen = new HashSet<(Guid, Guid)>();
        var reported = new HashSet<(Guid, Guid)>();
        var collapsed = new List<Connection>();
        foreach (Connection conn in connections)
        {
            (Guid, Guid) pair = (conn.FromNode, conn.ToNode);
            if (seen.Add(pair))
            {
                collapsed.Add(conn);
            }
            else if (reported.Add(pair))
            {
                duplicates.Add(conn);
            }
        }

        return collapsed;
    }

    /// <summary>Kahn's sort over the raw connections, seeded in node order; fewer ids than nodes means a cycle.</summary>
    private static List<Guid> TopologicalSort(PipelineGraph graph)
    {
        var inDegree = new Dictionary<Guid, int>();
        var adjacency = new Dictionary<Guid, List<Guid>>();

        foreach (NodeDefinition node in graph.Nodes)
        {
            inDegree[node.Id] = 0;
            adjacency[node.Id] = new List<Guid>();
        }

        foreach (Connection conn in graph.Connections)
        {
            adjacency[conn.FromNode].Add(conn.ToNode);
            inDegree[conn.ToNode]++;
        }

        var queue = new Queue<Guid>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var sorted = new List<Guid>();

        while (queue.Count > 0)
        {
            Guid current = queue.Dequeue();
            sorted.Add(current);

            foreach (Guid neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return sorted;
    }

    private static bool AddCategoryRefusals(
        List<Connection> connections,
        Dictionary<Guid, NodeDefinition> nodes,
        Dictionary<Guid, NodeCategory> categories,
        List<GraphIssue> issues)
    {
        bool refused = false;
        foreach (Connection conn in connections)
        {
            NodeDefinition from = nodes[conn.FromNode];
            NodeDefinition to = nodes[conn.ToNode];
            if (categories[to.Id] == NodeCategory.Source)
            {
                refused = true;
                issues.Add(new GraphIssue(
                    GraphIssueKind.ConnectionIntoSource,
                    GraphIssueSeverity.Refusal,
                    $"Connection from '{from.TypeKey}' ({from.Id}) into source node '{to.TypeKey}' ({to.Id}) is not allowed.",
                    to.Id,
                    to.TypeKey,
                    from.Id,
                    to.Id));
            }
            if (categories[from.Id] == NodeCategory.Output)
            {
                refused = true;
                issues.Add(new GraphIssue(
                    GraphIssueKind.ConnectionOutOfOutput,
                    GraphIssueSeverity.Refusal,
                    $"Connection out of output node '{from.TypeKey}' ({from.Id}) to '{to.TypeKey}' ({to.Id}) is not allowed.",
                    from.Id,
                    from.TypeKey,
                    from.Id,
                    to.Id));
            }
        }

        return refused;
    }

    private static void AddInertNodes(
        IList<NodeDefinition> nodes,
        List<Connection> connections,
        Dictionary<Guid, NodeCategory> categories,
        List<GraphIssue> issues)
    {
        HashSet<Guid> forward = Reach(
            nodes.Where(n => categories[n.Id] == NodeCategory.Source).Select(n => n.Id),
            connections.ToLookup(c => c.FromNode, c => c.ToNode));
        HashSet<Guid> backward = Reach(
            nodes.Where(n => categories[n.Id] == NodeCategory.Output).Select(n => n.Id),
            connections.ToLookup(c => c.ToNode, c => c.FromNode));

        foreach (NodeDefinition node in nodes.Where(n => !forward.Contains(n.Id) || !backward.Contains(n.Id)))
        {
            issues.Add(NodeRefusal(
                GraphIssueKind.InertNode,
                $"Node '{node.TypeKey}' ({node.Id}) is not on a path from a source to an output.",
                node));
        }
    }

    private static HashSet<Guid> Reach(IEnumerable<Guid> starts, ILookup<Guid, Guid> next)
    {
        var reached = new HashSet<Guid>();
        var pending = new Stack<Guid>(starts);
        while (pending.Count > 0)
        {
            Guid id = pending.Pop();
            if (reached.Add(id))
            {
                foreach (Guid neighbor in next[id])
                {
                    pending.Push(neighbor);
                }
            }
        }

        return reached;
    }

    private static GraphIssue GraphRefusal(GraphIssueKind kind, string message) =>
        new(kind, GraphIssueSeverity.Refusal, message, null, null, null, null);

    private static GraphIssue NodeRefusal(GraphIssueKind kind, string message, NodeDefinition node) =>
        new(kind, GraphIssueSeverity.Refusal, message, node.Id, node.TypeKey, null, null);
}
