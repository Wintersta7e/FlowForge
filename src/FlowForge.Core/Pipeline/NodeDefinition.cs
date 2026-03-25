using System.Text.Json;

namespace FlowForge.Core.Pipeline;

public class NodeDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Matches the node's registered TypeKey, e.g. "FolderInput".</summary>
    public string TypeKey { get; init; } = string.Empty;

    public CanvasPosition Position { get; set; } = new();

    /// <summary>Serialized node config. Each node type owns its own config class.</summary>
    public IDictionary<string, JsonElement> Config { get; init; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
