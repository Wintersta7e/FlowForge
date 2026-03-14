using FlowForge.Core.Models;

namespace FlowForge.Core.Execution;

public class ExecutionResult
{
    public bool IsDryRun { get; init; }
    public int TotalFiles { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// All processed jobs. Thread-safety contract: during pipeline execution,
    /// <see cref="PipelineRunner"/> holds <c>lock (result)</c> on the <see cref="ExecutionResult"/>
    /// instance when mutating this list and the counter properties. All reads happen after
    /// execution completes, so no concurrent read/write conflicts occur.
    /// <para/>
    /// Note: this list grows with every file in the pipeline and is unbounded.
    /// For very large pipelines (100k+ files), callers should consider streaming
    /// results via <see cref="PipelineProgressEvent"/> rather than inspecting this list.
    /// </summary>
    public List<FileJob> Jobs { get; init; } = new();
}
