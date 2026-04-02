using FlowForge.Core.Models;

namespace FlowForge.Core.Execution;

public sealed record FileProcessed(FileJob Job) : PipelineProgressEvent;
