namespace FlowForge.Core.Models;

public enum FileJobStatus { Pending, Processing, Succeeded, Failed, Skipped }

public class FileJob
{
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Path that never changes — the original source file.</summary>
    public string OriginalPath { get; init; } = string.Empty;

    /// <summary>Current working path — mutated by transform nodes.</summary>
    public string CurrentPath { get; set; } = string.Empty;

    /// <summary>Key-value metadata bag (EXIF, ID3, custom). Values are strings.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    public FileJobStatus Status { get; set; } = FileJobStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Ordered log of what each node did to this file.</summary>
    public List<string> NodeLog { get; init; } = new();

    public string FileName => Path.GetFileName(CurrentPath);
    public string Extension => Path.GetExtension(CurrentPath).ToLowerInvariant();
    public string DirectoryName => Path.GetDirectoryName(CurrentPath) ?? string.Empty;
}
