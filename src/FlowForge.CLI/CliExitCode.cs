using FlowForge.Core.Execution;

namespace FlowForge.CLI;

/// <summary>The CLI's process exit codes.</summary>
internal static class CliExitCode
{
    /// <summary>Every file succeeded or was skipped, or there were no files.</summary>
    internal const int Success = 0;

    /// <summary>Some files succeeded and some failed.</summary>
    internal const int Partial = 1;

    /// <summary>Every processed file failed, or nothing ran (invalid arguments, a load or configuration error, cancellation).</summary>
    internal const int Failure = 2;

    /// <summary>Maps a completed run to its exit code. Skipped files count toward neither success nor failure.</summary>
    internal static int FromResult(ExecutionResult result)
    {
        if (result.Failed > 0 && result.Succeeded > 0)
        {
            return Partial;
        }

        if (result.Failed > 0 && result.Succeeded == 0)
        {
            return Failure;
        }

        return Success;
    }
}
