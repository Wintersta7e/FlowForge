namespace FlowForge.Core.Execution;

/// <summary>
/// How far one transform step has got: <see cref="Done"/> of its <see cref="Total"/> input jobs,
/// reported when the step starts, every few jobs and when it ends. <see cref="Stage"/> counts the
/// run's transforms from 1 in the order they run. Run progress is counted by
/// <see cref="FileProcessed.CompletesOriginal"/>, never by this event.
/// </summary>
public sealed record StageProgress(string TypeKey, int Stage, int StageCount, int Done, int Total) : PipelineProgressEvent;
