namespace FlowForge.Core.Models;

public enum FileJobStatus { Pending, Processing, Succeeded, Failed, Skipped }

public class FileJob
{
    private string _currentPath = string.Empty;
    private string? _cachedExtension;
    private string? _cachedFileName;
    private string? _cachedDirectoryName;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Path that never changes — the original source file.</summary>
    public string OriginalPath { get; init; } = string.Empty;

    /// <summary>Current working path — mutated by transform nodes. Setting invalidates cached properties.</summary>
    public string CurrentPath
    {
        get => _currentPath;
        set
        {
            _currentPath = value;
            _cachedExtension = null;
            _cachedFileName = null;
            _cachedDirectoryName = null;
        }
    }

    /// <summary>Key-value metadata bag (EXIF, ID3, custom). Values are strings.</summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    public FileJobStatus Status { get; set; } = FileJobStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Ordered log of what each node did to this file.</summary>
    public List<string> NodeLog { get; init; } = new();

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string FileName => _cachedFileName ??= Path.GetFileName(_currentPath);

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string Extension => _cachedExtension ??= Path.GetExtension(_currentPath).ToLowerInvariant();

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string DirectoryName => _cachedDirectoryName ??= Path.GetDirectoryName(_currentPath) ?? string.Empty;
}
