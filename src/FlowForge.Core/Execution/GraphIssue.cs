namespace FlowForge.Core.Execution;

/// <summary>
/// One finding about a pipeline graph. Graph-level issues set none of the ids; per-node issues set
/// <see cref="NodeId"/> and <see cref="TypeKey"/>; per-connection issues set <see cref="FromNode"/> and
/// <see cref="ToNode"/>, plus the offending node's id and type when the problem is that node's category.
/// </summary>
public sealed record GraphIssue(
    GraphIssueKind Kind,
    GraphIssueSeverity Severity,
    string Message,
    Guid? NodeId,
    string? TypeKey,
    Guid? FromNode,
    Guid? ToNode);
