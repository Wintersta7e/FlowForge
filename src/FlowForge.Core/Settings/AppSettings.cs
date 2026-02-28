namespace FlowForge.Core.Settings;

/// <summary>
/// User preferences persisted between sessions.
/// All properties have sensible defaults so a fresh install works out of the box.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Default folder shown in file-open dialogs for pipeline input.</summary>
    public string DefaultInputFolder { get; set; } = string.Empty;

    /// <summary>Default folder shown in file-save dialogs for pipeline output.</summary>
    public string DefaultOutputFolder { get; set; } = string.Empty;

    /// <summary>Maximum number of files processed concurrently by the pipeline runner.</summary>
    public int MaxConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>Most recently opened pipeline files, newest first. Max 10.</summary>
    public List<string> RecentPipelines { get; set; } = new();

    private const int MaxRecentPipelines = 10;

    /// <summary>Add a pipeline path to the front of the recent list, deduplicating and trimming.</summary>
    public void AddRecentPipeline(string path)
    {
        RecentPipelines.Remove(path);
        RecentPipelines.Insert(0, path);
        if (RecentPipelines.Count > MaxRecentPipelines)
        {
            RecentPipelines.RemoveRange(MaxRecentPipelines, RecentPipelines.Count - MaxRecentPipelines);
        }
    }

    /// <summary>Clear all recent pipelines.</summary>
    public void ClearRecentPipelines() => RecentPipelines.Clear();
}
