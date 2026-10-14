using FlowForge.Core.Execution;
using FlowForge.Core.Models;

namespace FlowForge.CLI;

/// <summary>
/// Writes pipeline progress to the console: scan count, phase lines and one line per processed file.
/// Each report is written on the calling thread, so lines appear in the order the runner reports them.
/// </summary>
internal sealed class CliProgressReporter(TextWriter status, TextWriter error, bool verbose) : IProgress<PipelineProgressEvent>
{
    // Outputs report concurrently; one lock keeps each file's lines together.
    private readonly Lock _sync = new();
    private int _discoveredCount;

    public void Report(PipelineProgressEvent value)
    {
        lock (_sync)
        {
            switch (value)
            {
                case PhaseChanged { Phase: ExecutionPhase.Enumerating }:
                    status.Write("Scanning...");
                    break;

                case FilesDiscovered discovered:
                    _discoveredCount = discovered.TotalCount;
                    status.Write($"\rScanning... {discovered.TotalCount} files found");
                    break;

                case PhaseChanged { Phase: ExecutionPhase.Processing }:
                    status.WriteLine();
                    status.WriteLine($"Processing {_discoveredCount} files...");
                    status.WriteLine();
                    break;

                case FileProcessed { Job: var job }:
                    PrintFileResult(job);
                    break;

                case PhaseChanged { Phase: ExecutionPhase.Complete }:
                    break;
            }
        }
    }

    // Print the status line (and optional detail) for a single processed file.
    private void PrintFileResult(FileJob job)
    {
        string statusIcon = job.Status switch
        {
            FileJobStatus.Succeeded => "[OK]",
            FileJobStatus.Failed => "[FAIL]",
            FileJobStatus.Skipped => "[SKIP]",
            _ => "[??]"
        };

        status.WriteLine($"  {statusIcon} {Path.GetFileName(job.OriginalPath)}");

        if (verbose)
        {
            foreach (string logEntry in job.NodeLog)
            {
                status.WriteLine($"        {logEntry}");
            }
        }

        if (job.Status == FileJobStatus.Failed && job.ErrorMessage is not null)
        {
            error.WriteLine($"        Error: {job.ErrorMessage}");
        }
    }
}
