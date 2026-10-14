using System.Diagnostics;
using System.Text;
using FlowForge.CLI;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Runs the built CLI as a separate process, so a test sees the exit code the process
/// actually returns rather than the value of a method called in-process.
/// </summary>
internal static class CliProcess
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(60);

    internal static async Task<CliRun> RunAsync(IReadOnlyList<string> args, string workingDirectory, CancellationToken ct)
    {
        string? hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var startInfo = new ProcessStartInfo
        {
            FileName = !string.IsNullOrEmpty(hostPath) && File.Exists(hostPath) ? hostPath : "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDirectory,
        };
        startInfo.ArgumentList.Add(typeof(CliExitCode).Assembly.Location);
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");

        // Both streams are drained before waiting: a child blocked on a full pipe never exits.
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ExitTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException(
                $"The CLI did not exit within 60 s.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
                $"stderr:{Environment.NewLine}{await stderr}");
        }

        return new CliRun(process.ExitCode, await stdout, await stderr);
    }
}
