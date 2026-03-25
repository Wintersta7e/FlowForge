namespace FlowForge.Core.Pipeline;

public class PipelineGraph
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled Pipeline";
    public string Version { get; init; } = "1.0";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public IList<NodeDefinition> Nodes { get; init; } = new List<NodeDefinition>();
    public IList<Connection> Connections { get; init; } = new List<Connection>();
}
