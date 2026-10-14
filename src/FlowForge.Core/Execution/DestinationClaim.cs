namespace FlowForge.Core.Execution;

/// <summary>
/// Where an output writes a job's file and how. <see cref="BackupPath"/> is set only for
/// <see cref="ClaimKind.ReplaceWithBackup"/>.
/// </summary>
public sealed record DestinationClaim(string Path, ClaimKind Kind, string? BackupPath);
