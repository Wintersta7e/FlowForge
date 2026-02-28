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

    /// <summary>Full path of the last pipeline the user had open, or null if none.</summary>
    public string? LastOpenedPipeline { get; set; }
}
