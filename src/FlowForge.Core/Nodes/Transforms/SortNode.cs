using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Transforms;

/// <summary>
/// Buffers all incoming jobs, sorts them by the specified field, then yields them.
/// Implements IBufferedTransformNode to signal the runner that this node
/// needs all inputs collected before processing.
/// </summary>
public class SortNode : ITransformNode, IBufferedTransformNode
{
    private readonly ILogger<SortNode> _logger;

    public SortNode(ILogger<SortNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "Sort";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("field", ConfigFieldType.Select, Label: "Sort Field", DefaultValue: "filename",
            Options: new[] { "filename", "extension", "size", "createdAt", "modifiedAt" }, Description: "File property to sort by"),
        new ConfigField("direction", ConfigFieldType.Select, Label: "Direction", DefaultValue: "asc",
            Options: new[] { "asc", "desc" }, Description: "asc: smallest/oldest first, desc: largest/newest first"),
    };

    private string _field = "filename";
    private string _direction = "asc";
    private readonly List<FileJob> _buffer = new();

    public void Configure(IDictionary<string, JsonElement> config)
    {
        _buffer.Clear();

        if (config.TryGetValue("field", out JsonElement fieldElement) &&
            fieldElement.ValueKind == JsonValueKind.String)
        {
            _field = fieldElement.GetString() ?? "filename";
        }

        if (config.TryGetValue("direction", out JsonElement dirElement) &&
            dirElement.ValueKind == JsonValueKind.String)
        {
            _direction = dirElement.GetString() ?? "asc";
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _buffer.Add(job);

        // Return empty — jobs will be yielded from FlushAsync
        return Task.FromResult(Enumerable.Empty<FileJob>());
    }

    public Task<IEnumerable<FileJob>> FlushAsync(bool dryRun = false, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            bool descending = _direction.Equals("desc", StringComparison.OrdinalIgnoreCase);

            // Pre-compute sort keys once per job (H3 fix: avoids O(n log n) key-selector calls)
            List<FileJob> result = _field.ToLowerInvariant() switch
            {
                "filename" => SortByKey(_buffer, j => j.FileName, descending, StringComparer.OrdinalIgnoreCase),
                "extension" => SortByKey(_buffer, j => j.Extension, descending, StringComparer.OrdinalIgnoreCase),
                "size" => SortByKey(_buffer, j => GetFileSize(j.CurrentPath, dryRun), descending, Comparer<long>.Default),
                "createdat" => SortByKey(_buffer, j => GetFileCreatedAt(j.CurrentPath, dryRun), descending, Comparer<DateTime>.Default),
                "modifiedat" => SortByKey(_buffer, j => GetFileModifiedAt(j.CurrentPath, dryRun), descending, Comparer<DateTime>.Default),
                _ => throw new InvalidOperationException($"Unknown sort field: '{_field}'")
            };

            foreach (FileJob job in result)
            {
                job.NodeLog.Add($"Sort: ordered by {_field} {_direction}");
            }

            return Task.FromResult<IEnumerable<FileJob>>(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Sort: flush failed, returning {Count} buffered jobs as failed", _buffer.Count);
            foreach (FileJob job in _buffer)
            {
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = $"Sort: failed during flush — {ex.Message}";
            }
            return Task.FromResult<IEnumerable<FileJob>>(_buffer.ToList());
        }
        finally
        {
            _buffer.Clear();
        }
    }

    /// <summary>
    /// Pre-computes sort keys once per job, then sorts by the pre-computed key.
    /// This avoids O(n log n) key-selector calls during LINQ's sort (H3).
    /// </summary>
    private static List<FileJob> SortByKey<TKey>(
        List<FileJob> jobs,
        Func<FileJob, TKey> keySelector,
        bool descending,
        IComparer<TKey> comparer)
    {
        var keyed = jobs.Select(j => (job: j, key: keySelector(j))).ToList();
        keyed.Sort((a, b) =>
        {
            int cmp = comparer.Compare(a.key, b.key);
            return descending ? -cmp : cmp;
        });
        return keyed.Select(x => x.job).ToList();
    }

    private long GetFileSize(string path, bool dryRun)
    {
        if (dryRun)
        {
            return 0;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Sort: file not found at '{FilePath}', using default size 0", path);
            return 0;
        }

        return new FileInfo(path).Length;
    }

    private DateTime GetFileCreatedAt(string path, bool dryRun)
    {
        if (dryRun)
        {
            return DateTime.MinValue;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Sort: file not found at '{FilePath}', using default date", path);
            return DateTime.MinValue;
        }

        return File.GetCreationTimeUtc(path);
    }

    private DateTime GetFileModifiedAt(string path, bool dryRun)
    {
        if (dryRun)
        {
            return DateTime.MinValue;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning("Sort: file not found at '{FilePath}', using default date", path);
            return DateTime.MinValue;
        }

        return File.GetLastWriteTimeUtc(path);
    }
}
