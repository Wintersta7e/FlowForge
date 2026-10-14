using FlowForge.Core.Execution;

namespace FlowForge.Tests.Helpers;

/// <summary>Run contexts for calling nodes directly, outside a runner.</summary>
public static class TestRunContext
{
    /// <summary>
    /// A live run. Without <paramref name="stagingRoot"/> the root is a fresh temp path that only a
    /// staging write would ever create.
    /// </summary>
    public static RunContext Live(string? stagingRoot = null) => new(dryRun: false, stagingRoot ?? FreshRoot());

    public static RunContext Dry() => new(dryRun: true, FreshRoot());

    private static string FreshRoot() =>
        Path.Combine(Path.GetTempPath(), "FlowForge_TestStaging_" + Guid.NewGuid().ToString("N"));
}
