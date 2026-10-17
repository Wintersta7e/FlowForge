using System.Text.Json.Serialization;

namespace FlowForge.Core.Models;

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

    /// <summary>
    /// Where the job's bytes are: the original until a transform rewrites the content, then a staged copy.
    /// <see cref="CurrentPath"/> carries the name; this carries the content. Left out of the CLI's
    /// JSON: a staged path is deleted before the result is printed.
    /// </summary>
    [JsonIgnore]
    public string ContentPath { get; set; } = string.Empty;

    /// <summary>Key-value metadata bag (EXIF, ID3, custom). Values are strings.</summary>
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public FileJobStatus Status { get; set; } = FileJobStatus.Pending;
    public string? ErrorMessage { get; set; }

    /// <summary>Null in a real run; in a dry run, why this job's preview may differ from the real run.</summary>
    public PreviewMarker? PreviewMarker { get; set; }

    /// <summary>Ordered log of what each node did to this file.</summary>
    public IList<string> NodeLog { get; init; } = new List<string>();

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string FileName => _cachedFileName ??= Path.GetFileName(_currentPath);

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string Extension => _cachedExtension ??= Path.GetExtension(_currentPath).ToLowerInvariant();

    /// <summary>Cached — recomputed lazily when <see cref="CurrentPath"/> changes.</summary>
    public string DirectoryName => _cachedDirectoryName ??= Path.GetDirectoryName(_currentPath) ?? string.Empty;

    /// <summary>
    /// A copy for one more outgoing connection: same paths and status, its own id, and its own
    /// metadata and log so one branch's changes never reach another.
    /// </summary>
    internal FileJob CloneForBranch() => new()
    {
        OriginalPath = OriginalPath,
        ContentPath = ContentPath,
        CurrentPath = CurrentPath,
        Status = Status,
        PreviewMarker = PreviewMarker,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal),
        NodeLog = new List<string>(NodeLog),
    };
}
