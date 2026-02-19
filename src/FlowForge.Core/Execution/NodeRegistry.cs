using System.Text.Json;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Nodes.Sources;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Outputs;
using FlowForge.Core.Pipeline;

namespace FlowForge.Core.Execution;

public class NodeRegistry
{
    private readonly Dictionary<string, Func<object>> _factories = new();

    public void Register<T>(string typeKey, Func<T> factory) where T : class
    {
        _factories[typeKey] = factory;
    }

    public ISourceNode GetSource(NodeDefinition def)
    {
        object instance = CreateInstance(def);
        if (instance is not ISourceNode source)
        {
            throw new InvalidOperationException($"Node '{def.TypeKey}' is not an ISourceNode.");
        }
        return source;
    }

    public ITransformNode GetTransform(NodeDefinition def)
    {
        object instance = CreateInstance(def);
        if (instance is not ITransformNode transform)
        {
            throw new InvalidOperationException($"Node '{def.TypeKey}' is not an ITransformNode.");
        }
        return transform;
    }

    public IOutputNode GetOutput(NodeDefinition def)
    {
        object instance = CreateInstance(def);
        if (instance is not IOutputNode output)
        {
            throw new InvalidOperationException($"Node '{def.TypeKey}' is not an IOutputNode.");
        }
        return output;
    }

    public bool IsRegistered(string typeKey) => _factories.ContainsKey(typeKey);

    public NodeCategory GetCategory(NodeDefinition def)
    {
        object instance = CreateInstance(def, configure: false);
        if (instance is ISourceNode) return NodeCategory.Source;
        if (instance is ITransformNode) return NodeCategory.Transform;
        if (instance is IOutputNode) return NodeCategory.Output;
        throw new InvalidOperationException($"Node '{def.TypeKey}' does not implement any known node interface.");
    }

    private object CreateInstance(NodeDefinition def, bool configure = true)
    {
        if (!_factories.TryGetValue(def.TypeKey, out Func<object>? factory))
        {
            throw new InvalidOperationException($"No node registered for TypeKey '{def.TypeKey}'.");
        }

        object instance = factory();

        if (configure)
        {
            if (instance is ISourceNode source) source.Configure(def.Config);
            else if (instance is ITransformNode transform) transform.Configure(def.Config);
            else if (instance is IOutputNode output) output.Configure(def.Config);
        }

        return instance;
    }

    public static NodeRegistry CreateDefault()
    {
        var registry = new NodeRegistry();
        registry.Register<FolderInputNode>("FolderInput", () => new FolderInputNode());
        registry.Register<RenamePatternNode>("RenamePattern", () => new RenamePatternNode());
        registry.Register<FolderOutputNode>("FolderOutput", () => new FolderOutputNode());
        return registry;
    }
}

public enum NodeCategory { Source, Transform, Output }
