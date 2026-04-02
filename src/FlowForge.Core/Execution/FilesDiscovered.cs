namespace FlowForge.Core.Execution;

public sealed record FilesDiscovered(int TotalCount) : PipelineProgressEvent;
