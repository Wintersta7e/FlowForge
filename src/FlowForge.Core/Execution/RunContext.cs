namespace FlowForge.Core.Execution;

/// <summary>The state one pipeline run shares with every node it calls.</summary>
public sealed class RunContext : IDisposable
{
    public RunContext(bool dryRun, string stagingRoot)
    {
        ArgumentNullException.ThrowIfNull(stagingRoot);
        if (!Path.IsPathFullyQualified(stagingRoot))
        {
            throw new ArgumentException($"The staging root must be a fully qualified path: '{stagingRoot}'.", nameof(stagingRoot));
        }

        DryRun = dryRun;
        StagingRoot = stagingRoot;
        RunId = Guid.NewGuid().ToString("N");
    }

    public bool DryRun { get; }

    /// <summary>32 lowercase hex digits, unique to this run.</summary>
    public string RunId { get; }

    /// <summary>Every destination this run has claimed, across all its outputs.</summary>
    public DestinationRegistry Destinations { get; } = new();

    internal string StagingRoot { get; }

    public void Dispose()
    {
    }
}
