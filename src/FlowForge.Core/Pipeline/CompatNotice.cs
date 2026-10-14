namespace FlowForge.Core.Pipeline;

/// <summary>
/// A behaviour change that applies to a pipeline loaded from an older file version.
/// <see cref="NodeId"/> and <see cref="TypeKey"/> are null when the change applies to the whole pipeline.
/// </summary>
public sealed record CompatNotice(string Code, Guid? NodeId, string? TypeKey, string Message);
