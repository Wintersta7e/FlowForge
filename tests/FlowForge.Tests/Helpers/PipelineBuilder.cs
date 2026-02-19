using System.Text.Json;
using FlowForge.Core.Pipeline;

namespace FlowForge.Tests.Helpers;

public class PipelineBuilder
{
    private readonly PipelineGraph _graph;
    private readonly List<NodeDefinition> _orderedNodes = new();

    private PipelineBuilder(string name)
    {
        _graph = new PipelineGraph { Name = name };
    }

    public static PipelineBuilder New(string name = "Test Pipeline") => new(name);

    public PipelineBuilder AddSource(string typeKey, object config)
    {
        NodeDefinition node = CreateNode(typeKey, config);
        _orderedNodes.Add(node);
        _graph.Nodes.Add(node);
        return this;
    }

    public PipelineBuilder AddTransform(string typeKey, object config)
    {
        NodeDefinition node = CreateNode(typeKey, config);
        _orderedNodes.Add(node);
        _graph.Nodes.Add(node);
        return this;
    }

    public PipelineBuilder AddOutput(string typeKey, object config)
    {
        NodeDefinition node = CreateNode(typeKey, config);
        _orderedNodes.Add(node);
        _graph.Nodes.Add(node);
        return this;
    }

    public PipelineGraph Build()
    {
        // Auto-wire linear connections between sequential nodes
        for (int i = 0; i < _orderedNodes.Count - 1; i++)
        {
            _graph.Connections.Add(new Connection
            {
                FromNode = _orderedNodes[i].Id,
                ToNode = _orderedNodes[i + 1].Id
            });
        }

        return _graph;
    }

    private static NodeDefinition CreateNode(string typeKey, object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> configDict = doc.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        return new NodeDefinition
        {
            TypeKey = typeKey,
            Config = configDict
        };
    }
}
