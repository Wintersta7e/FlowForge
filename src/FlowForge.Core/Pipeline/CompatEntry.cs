using System.Text.Json;

namespace FlowForge.Core.Pipeline;

/// <summary>
/// A behaviour change reported for files saved at <see cref="Version"/> or earlier. With a null
/// <see cref="TypeKey"/> the entry covers the whole pipeline and <see cref="GraphPredicate"/> decides;
/// otherwise <see cref="ConfigPredicate"/> decides for each node of that type.
/// </summary>
internal sealed record CompatEntry(
    string Code,
    string Version,
    string? TypeKey,
    Func<PipelineGraph, bool>? GraphPredicate,
    Func<IDictionary<string, JsonElement>, bool>? ConfigPredicate,
    string Message);
