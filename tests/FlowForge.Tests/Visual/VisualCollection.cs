namespace FlowForge.Tests.Visual;

/// <summary>
/// The one collection for visual tests. It never runs in parallel with other tests:
/// XDG_CONFIG_HOME is process-wide and the frame store is static.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Names an xUnit test collection.")]
public static class VisualCollection
{
    public const string Name = "Visual";
}
