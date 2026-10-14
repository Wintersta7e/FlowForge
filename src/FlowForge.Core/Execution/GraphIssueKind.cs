namespace FlowForge.Core.Execution;

public enum GraphIssueKind
{
    NoNodes,
    UnknownNodeType,
    UnknownSourceNode,
    UnknownTargetNode,
    Cycle,
    DuplicateConnection,
    ConnectionIntoSource,
    ConnectionOutOfOutput,
    InertNode,
}
