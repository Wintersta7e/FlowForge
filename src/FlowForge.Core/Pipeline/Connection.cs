namespace FlowForge.Core.Pipeline;

public class Connection
{
    public Guid FromNode { get; init; }
    public string FromPin { get; init; } = "out";
    public Guid ToNode { get; init; }
    public string ToPin { get; init; } = "in";
}
