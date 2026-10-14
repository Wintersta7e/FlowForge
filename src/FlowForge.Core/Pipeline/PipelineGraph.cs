using System.Text.Json.Serialization;

namespace FlowForge.Core.Pipeline;

public class PipelineGraph
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Pipeline";
    public string Version { get; init; } = KnownVersions.Current;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public IList<NodeDefinition> Nodes { get; init; } = new List<NodeDefinition>();
    public IList<Connection> Connections { get; init; } = new List<Connection>();

    /// <summary>The behaviour changes that apply because the file was saved by an older version; set only on load.</summary>
    [JsonIgnore]
    public IReadOnlyList<CompatNotice> LoadNotices { get; internal set; } = [];
}
