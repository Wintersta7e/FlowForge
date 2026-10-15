namespace FlowForge.Core.Models;

/// <summary>Why a dry run's preview of a job may differ from what the real run does. Set only in a dry run.</summary>
public enum PreviewMarker
{
    /// <summary>The job passed an image node: the real run opens the file there and may still refuse it.</summary>
    CheckedAtRun,

    /// <summary>The preview depends on something only the real run can read, so its name or order may differ.</summary>
    Unresolved,
}
