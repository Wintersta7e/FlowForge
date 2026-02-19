using System.Text.Json;

namespace FlowForge.Core.Pipeline;

public class PipelineGraph
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Pipeline";
    public string Version { get; init; } = "1.0";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<NodeDefinition> Nodes { get; init; } = new();
    public List<Connection> Connections { get; init; } = new();
}

public class NodeDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Matches the node's registered TypeKey, e.g. "FolderInput".</summary>
    public string TypeKey { get; init; } = string.Empty;

    public CanvasPosition Position { get; set; } = new();

    /// <summary>Serialized node config. Each node type owns its own config class.</summary>
    public Dictionary<string, JsonElement> Config { get; init; } = new();
}

public class Connection
{
    public Guid FromNode { get; init; }
    public string FromPin { get; init; } = "out";
    public Guid ToNode { get; init; }
    public string ToPin { get; init; } = "in";
}

public record CanvasPosition(double X = 0, double Y = 0);
