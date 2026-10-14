namespace FlowForge.Core.Settings;

/// <summary>
/// User preferences persisted between sessions.
/// All properties have sensible defaults so a fresh install works out of the box.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Most recently opened pipeline files, newest first. Max 10.</summary>
    public IList<string> RecentPipelines { get; set; } = new List<string>();

    private const int MaxRecentPipelines = 10;

    /// <summary>Add a pipeline path to the front of the recent list, deduplicating and trimming.</summary>
    public void AddRecentPipeline(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Case-insensitive dedup for Windows path compatibility
        for (int i = RecentPipelines.Count - 1; i >= 0; i--)
        {
            if (string.Equals(RecentPipelines[i], path, StringComparison.OrdinalIgnoreCase))
            {
                RecentPipelines.RemoveAt(i);
            }
        }

        RecentPipelines.Insert(0, path);
        while (RecentPipelines.Count > MaxRecentPipelines)
        {
            RecentPipelines.RemoveAt(RecentPipelines.Count - 1);
        }
    }

    /// <summary>Clear all recent pipelines.</summary>
    public void ClearRecentPipelines() => RecentPipelines.Clear();

    /// <summary>Clamp settings values to safe ranges after deserialization.</summary>
    public void Validate()
    {
        RecentPipelines ??= new List<string>();

        // Filter out invalid recent pipeline entries (corrupted settings, null bytes, extreme length)
        for (int i = RecentPipelines.Count - 1; i >= 0; i--)
        {
            string entry = RecentPipelines[i];
            if (string.IsNullOrWhiteSpace(entry) ||
                entry.Length > 4096 ||
                entry.Contains('\0', StringComparison.Ordinal) ||
                !Path.IsPathFullyQualified(entry))
            {
                RecentPipelines.RemoveAt(i);
            }
        }

        while (RecentPipelines.Count > MaxRecentPipelines)
        {
            RecentPipelines.RemoveAt(RecentPipelines.Count - 1);
        }
    }
}
