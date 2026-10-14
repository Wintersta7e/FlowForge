namespace FlowForge.Tests.Helpers;

/// <summary>The exit code and captured output of one spawned CLI run.</summary>
internal sealed record CliRun(int ExitCode, string StdOut, string StdErr);
