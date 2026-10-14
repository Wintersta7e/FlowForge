namespace FlowForge.Core.Pipeline;

/// <summary>The pipeline file versions this build reads, oldest first. Every save writes <see cref="Current"/>.</summary>
public static class KnownVersions
{
    public static IReadOnlyList<string> All { get; } = ["1.0", "2.0"];

    public static string Current { get; } = All[^1];
}
