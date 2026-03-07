using FlowForge.Core.Models;

namespace FlowForge.Core.Execution;

public enum ExecutionPhase
{
    Enumerating,
    Processing,
    Complete,
}

public abstract record PipelineProgressEvent;

public sealed record FilesDiscovered(int TotalCount) : PipelineProgressEvent;

public sealed record FileProcessed(FileJob Job) : PipelineProgressEvent;

public sealed record PhaseChanged(ExecutionPhase Phase) : PipelineProgressEvent;
