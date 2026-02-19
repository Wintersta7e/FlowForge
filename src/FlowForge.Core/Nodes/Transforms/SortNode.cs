using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Transforms;

/// <summary>
/// Buffers all incoming jobs, sorts them by the specified field, then yields them.
/// Implements IBufferedTransformNode to signal the runner that this node
/// needs all inputs collected before processing.
/// </summary>
public class SortNode : ITransformNode, IBufferedTransformNode
{
    public string TypeKey => "Sort";

    private string _field = "filename";
    private string _direction = "asc";
    private readonly List<FileJob> _buffer = new();

    public void Configure(Dictionary<string, JsonElement> config)
    {
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

    public Task<IEnumerable<FileJob>> FlushAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        bool descending = _direction.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IEnumerable<FileJob> sorted = _field.ToLowerInvariant() switch
        {
            "filename" => descending
                ? _buffer.OrderByDescending(j => j.FileName, StringComparer.OrdinalIgnoreCase)
                : _buffer.OrderBy(j => j.FileName, StringComparer.OrdinalIgnoreCase),
            "extension" => descending
                ? _buffer.OrderByDescending(j => j.Extension, StringComparer.OrdinalIgnoreCase)
                : _buffer.OrderBy(j => j.Extension, StringComparer.OrdinalIgnoreCase),
            "size" => descending
                ? _buffer.OrderByDescending(j => GetFileSize(j.CurrentPath))
                : _buffer.OrderBy(j => GetFileSize(j.CurrentPath)),
            "createdat" => descending
                ? _buffer.OrderByDescending(j => GetFileCreatedAt(j.CurrentPath))
                : _buffer.OrderBy(j => GetFileCreatedAt(j.CurrentPath)),
            "modifiedat" => descending
                ? _buffer.OrderByDescending(j => GetFileModifiedAt(j.CurrentPath))
                : _buffer.OrderBy(j => GetFileModifiedAt(j.CurrentPath)),
            _ => throw new InvalidOperationException($"Unknown sort field: '{_field}'")
        };

        List<FileJob> result = sorted.ToList();

        foreach (FileJob job in result)
        {
            job.NodeLog.Add($"Sort: ordered by {_field} {_direction}");
        }

        _buffer.Clear();
        return Task.FromResult<IEnumerable<FileJob>>(result);
    }

    private static long GetFileSize(string path)
    {
        if (!File.Exists(path)) return 0;
        return new FileInfo(path).Length;
    }

    private static DateTime GetFileCreatedAt(string path)
    {
        if (!File.Exists(path)) return DateTime.MinValue;
        return File.GetCreationTimeUtc(path);
    }

    private static DateTime GetFileModifiedAt(string path)
    {
        if (!File.Exists(path)) return DateTime.MinValue;
        return File.GetLastWriteTimeUtc(path);
    }
}
