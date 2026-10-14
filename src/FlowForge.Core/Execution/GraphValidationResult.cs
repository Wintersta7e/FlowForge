using FlowForge.Core.Pipeline;

namespace FlowForge.Core.Execution;

public sealed class GraphValidationResult
{
    internal GraphValidationResult(IReadOnlyList<GraphIssue> issues, IReadOnlyList<Connection> connections, IReadOnlyList<Guid> order)
    {
        Issues = issues;
        Refusals = issues.Where(i => i.Severity == GraphIssueSeverity.Refusal).ToList();
        Warnings = issues.Where(i => i.Severity == GraphIssueSeverity.Warning).ToList();
        Connections = connections;
        Order = order;
    }

    public IReadOnlyList<GraphIssue> Issues { get; }

    public IReadOnlyList<GraphIssue> Refusals { get; }

    public IReadOnlyList<GraphIssue> Warnings { get; }

    public bool IsValid => Refusals.Count == 0;

    /// <summary>The graph's connections with each repeated (from, to) pair kept once, at its first occurrence.</summary>
    internal IReadOnlyList<Connection> Connections { get; }

    /// <summary>Node ids in topological order; empty when the graph was refused before sorting finished.</summary>
    internal IReadOnlyList<Guid> Order { get; }
}
