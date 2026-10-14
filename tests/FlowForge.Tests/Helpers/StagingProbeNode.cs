using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// A transform that stages 16 bytes for each job, points the job at them, then succeeds, fails,
/// cancels the run or blocks until released.
/// </summary>
internal sealed class StagingProbeNode : ITransformNode
{
    public const string RegisteredTypeKey = "StagingProbe";

    public enum Outcome
    {
        Succeed,
        Fail,
        Cancel,
        Block,
    }

    public string TypeKey => RegisteredTypeKey;

    public Outcome Behaviour { get; init; }

    /// <summary>The run to cancel for <see cref="Outcome.Cancel"/>.</summary>
    public CancellationTokenSource? Cancellation { get; init; }

    /// <summary>Completed with the staged path when a <see cref="Outcome.Block"/> probe stages its file.</summary>
    public TaskCompletionSource<string> Staged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completing it lets a <see cref="Outcome.Block"/> probe go on.</summary>
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public UnixFileMode? ObservedRunDirectoryMode { get; private set; }

    public void Configure(IDictionary<string, JsonElement> config)
    {
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
    {
        string staged = context.NewStagingPath(".bin");
        await File.WriteAllBytesAsync(staged, new byte[16], ct);
        context.SetContent(job, staged);
        if (!OperatingSystem.IsWindows())
        {
            ObservedRunDirectoryMode = File.GetUnixFileMode(Path.GetDirectoryName(staged)!);
        }

        switch (Behaviour)
        {
            case Outcome.Fail:
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = "StagingProbe: failed on purpose.";
                break;
            case Outcome.Cancel:
                await Cancellation!.CancelAsync();
                ct.ThrowIfCancellationRequested();
                break;
            case Outcome.Block:
                Staged.TrySetResult(staged);
                await Release.Task.WaitAsync(ct);
                break;
        }

        return [job];
    }
}
