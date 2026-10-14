namespace FlowForge.Core.Execution;

/// <summary>How an output writes a claimed destination.</summary>
public enum ClaimKind
{
    /// <summary>The destination does not exist; delivery fails rather than replace one that appears meanwhile.</summary>
    New,

    /// <summary>The destination exists and is replaced.</summary>
    Replace,

    /// <summary>The destination exists; it becomes the backup and is replaced.</summary>
    ReplaceWithBackup,
}
