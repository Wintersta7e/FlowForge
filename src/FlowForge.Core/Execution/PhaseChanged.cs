namespace FlowForge.Core.Execution;

public sealed record PhaseChanged(ExecutionPhase Phase) : PipelineProgressEvent;
