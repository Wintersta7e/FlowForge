using FlowForge.Core.Models;

namespace FlowForge.Core.Execution;

public sealed record FileProcessed(FileJob Job) : PipelineProgressEvent
{
    /// <summary>
    /// True when this terminal ends the last live branch of its original; counting these events
    /// over <see cref="ExecutionResult.TotalFiles"/> gives run progress.
    /// </summary>
    public bool CompletesOriginal { get; init; } = true;
}
