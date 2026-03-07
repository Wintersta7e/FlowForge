using System.Text.Json;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Nodes.Outputs;
using FlowForge.Core.Nodes.Sources;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Pipeline;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

public class NodeRegistry
{
    private readonly Dictionary<string, Func<object>> _factories = new();
    private readonly Dictionary<string, NodeRegistration> _registrations = new();

    /// <summary>
    /// Registers a node factory with full metadata (display name, category, config schema).
    /// </summary>
    public void Register<T>(
        string typeKey,
        Func<T> factory,
        string displayName,
        NodeCategory category,
        Func<IReadOnlyList<ConfigField>> configSchemaGetter) where T : class
    {
        _factories[typeKey] = factory;
        _registrations[typeKey] = new NodeRegistration(displayName, category, configSchemaGetter);
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
        return GetCategoryForTypeKey(def.TypeKey);
    }

    /// <summary>
    /// Returns the config schema for a registered type key without creating a node instance.
    /// </summary>
    public IReadOnlyList<ConfigField> GetConfigSchema(string typeKey)
    {
        if (!_registrations.TryGetValue(typeKey, out NodeRegistration? registration))
        {
            throw new InvalidOperationException($"No registration metadata for TypeKey '{typeKey}'.");
        }
        return registration.ConfigSchemaGetter();
    }

    /// <summary>
    /// Returns the node category for a registered type key without creating a node instance.
    /// </summary>
    public NodeCategory GetCategoryForTypeKey(string typeKey)
    {
        if (!_registrations.TryGetValue(typeKey, out NodeRegistration? registration))
        {
            throw new InvalidOperationException($"No registration metadata for TypeKey '{typeKey}'.");
        }
        return registration.Category;
    }

    /// <summary>
    /// Returns the human-readable display name for a registered type key.
    /// </summary>
    public string GetDisplayName(string typeKey)
    {
        if (!_registrations.TryGetValue(typeKey, out NodeRegistration? registration))
        {
            throw new InvalidOperationException($"No registration metadata for TypeKey '{typeKey}'.");
        }
        return registration.DisplayName;
    }

    /// <summary>
    /// Returns all registered type keys.
    /// </summary>
    public IEnumerable<string> GetRegisteredTypeKeys()
    {
        return _registrations.Keys;
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
            if (instance is ISourceNode source)
                source.Configure(def.Config);
            else if (instance is ITransformNode transform)
                transform.Configure(def.Config);
            else if (instance is IOutputNode output)
                output.Configure(def.Config);
        }

        return instance;
    }

    public static NodeRegistry CreateDefault(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var registry = new NodeRegistry();

        // Sources
        registry.Register<FolderInputNode>("FolderInput", () => new FolderInputNode(loggerFactory.CreateLogger<FolderInputNode>()),
            "Folder Input", NodeCategory.Source, () => FolderInputNode.ConfigSchema);

        // Transforms -- Rename
        registry.Register<RenamePatternNode>("RenamePattern", () => new RenamePatternNode(loggerFactory.CreateLogger<RenamePatternNode>()),
            "Rename (Pattern)", NodeCategory.Transform, () => RenamePatternNode.ConfigSchema);
        registry.Register<RenameRegexNode>("RenameRegex", () => new RenameRegexNode(loggerFactory.CreateLogger<RenameRegexNode>()),
            "Rename (Regex)", NodeCategory.Transform, () => RenameRegexNode.ConfigSchema);
        registry.Register<RenameAddAffixNode>("RenameAddAffix", () => new RenameAddAffixNode(loggerFactory.CreateLogger<RenameAddAffixNode>()),
            "Rename (Add Affix)", NodeCategory.Transform, () => RenameAddAffixNode.ConfigSchema);

        // Transforms -- Filter & Sort
        registry.Register<FilterNode>("Filter", () => new FilterNode(loggerFactory.CreateLogger<FilterNode>()),
            "Filter", NodeCategory.Transform, () => FilterNode.ConfigSchema);
        registry.Register<SortNode>("Sort", () => new SortNode(loggerFactory.CreateLogger<SortNode>()),
            "Sort", NodeCategory.Transform, () => SortNode.ConfigSchema);

        // Transforms -- Image
        registry.Register<ImageResizeNode>("ImageResize", () => new ImageResizeNode(loggerFactory.CreateLogger<ImageResizeNode>()),
            "Image Resize", NodeCategory.Transform, () => ImageResizeNode.ConfigSchema);
        registry.Register<ImageConvertNode>("ImageConvert", () => new ImageConvertNode(loggerFactory.CreateLogger<ImageConvertNode>()),
            "Image Convert", NodeCategory.Transform, () => ImageConvertNode.ConfigSchema);
        registry.Register<ImageCompressNode>("ImageCompress", () => new ImageCompressNode(loggerFactory.CreateLogger<ImageCompressNode>()),
            "Image Compress", NodeCategory.Transform, () => ImageCompressNode.ConfigSchema);

        // Transforms -- Metadata
        registry.Register<MetadataExtractNode>("MetadataExtract", () => new MetadataExtractNode(loggerFactory.CreateLogger<MetadataExtractNode>()),
            "Metadata Extract", NodeCategory.Transform, () => MetadataExtractNode.ConfigSchema);

        // Outputs
        registry.Register<FolderOutputNode>("FolderOutput", () => new FolderOutputNode(loggerFactory.CreateLogger<FolderOutputNode>()),
            "Folder Output", NodeCategory.Output, () => FolderOutputNode.ConfigSchema);

        return registry;
    }

    private sealed record NodeRegistration(
        string DisplayName,
        NodeCategory Category,
        Func<IReadOnlyList<ConfigField>> ConfigSchemaGetter);
}

public enum NodeCategory { Source, Transform, Output }
