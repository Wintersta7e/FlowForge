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
    public List<FileJob> Jobs { get; init; } = new();
}
