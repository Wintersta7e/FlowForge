using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;

namespace FlowForge.Tests.Execution;

public class NodeRegistryTests
{
    private static readonly string[] AllTypeKeys =
    [
        "FolderInput",
        "RenamePattern",
        "RenameRegex",
        "RenameAddAffix",
        "Filter",
        "Sort",
        "ImageResize",
        "ImageConvert",
        "ImageCompress",
        "MetadataExtract",
        "FolderOutput"
    ];

    private static NodeDefinition MakeDef(string typeKey, object? config = null)
    {
        var configDict = new Dictionary<string, JsonElement>();
        if (config != null)
        {
            string json = JsonSerializer.Serialize(config);
            JsonDocument doc = JsonDocument.Parse(json);
            configDict = doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone());
        }
        return new NodeDefinition { TypeKey = typeKey, Config = configDict };
    }

    [Fact]
    public void CreateDefault_registers_all_11_node_types()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.GetRegisteredTypeKeys().Should().HaveCount(11);
    }

    [Theory]
    [MemberData(nameof(AllTypeKeysData))]
    public void IsRegistered_returns_true_for_all_default_keys(string typeKey)
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.IsRegistered(typeKey).Should().BeTrue();
    }

    [Fact]
    public void GetSource_returns_ISourceNode_for_FolderInput()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("FolderInput", new { path = "/tmp" });

        ISourceNode source = registry.GetSource(def);

        source.Should().NotBeNull();
        source.Should().BeAssignableTo<ISourceNode>();
    }

    [Fact]
    public void GetTransform_returns_ITransformNode_for_RenamePattern()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("RenamePattern", new { pattern = "{name}{ext}" });

        ITransformNode transform = registry.GetTransform(def);

        transform.Should().NotBeNull();
        transform.Should().BeAssignableTo<ITransformNode>();
    }

    [Fact]
    public void GetOutput_returns_IOutputNode_for_FolderOutput()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("FolderOutput", new { path = "/tmp" });

        IOutputNode output = registry.GetOutput(def);

        output.Should().NotBeNull();
        output.Should().BeAssignableTo<IOutputNode>();
    }

    [Fact]
    public void IsRegistered_returns_false_for_unknown_key()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.IsRegistered("TotallyFakeNode").Should().BeFalse();
    }

    [Fact]
    public void GetCategoryForTypeKey_returns_Source_for_FolderInput()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.GetCategoryForTypeKey("FolderInput").Should().Be(NodeCategory.Source);
    }

    [Fact]
    public void GetCategoryForTypeKey_returns_Transform_for_Filter()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.GetCategoryForTypeKey("Filter").Should().Be(NodeCategory.Transform);
    }

    [Fact]
    public void GetCategoryForTypeKey_returns_Output_for_FolderOutput()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        registry.GetCategoryForTypeKey("FolderOutput").Should().Be(NodeCategory.Output);
    }

    [Theory]
    [MemberData(nameof(AllTypeKeysData))]
    public void GetDisplayName_returns_non_empty_string_for_all_keys(string typeKey)
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        string displayName = registry.GetDisplayName(typeKey);

        displayName.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(AllTypeKeysData))]
    public void GetConfigSchema_returns_non_null_non_empty_list_for_all_keys(string typeKey)
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();

        IReadOnlyList<ConfigField> schema = registry.GetConfigSchema(typeKey);

        schema.Should().NotBeNull();
        schema.Should().NotBeEmpty();
    }

    [Fact]
    public void GetSource_throws_for_unregistered_typeKey()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("NonExistent");

        Action act = () => registry.GetSource(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No node registered*");
    }

    [Fact]
    public void GetTransform_throws_for_unregistered_typeKey()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("NonExistent");

        Action act = () => registry.GetTransform(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No node registered*");
    }

    [Fact]
    public void GetOutput_throws_for_unregistered_typeKey()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("NonExistent");

        Action act = () => registry.GetOutput(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No node registered*");
    }

    [Fact]
    public void GetSource_throws_when_node_is_transform()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("RenamePattern", new { pattern = "{name}{ext}" });

        Action act = () => registry.GetSource(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not an ISourceNode*");
    }

    [Fact]
    public void GetTransform_throws_when_node_is_source()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("FolderInput", new { path = "/tmp" });

        Action act = () => registry.GetTransform(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not an ITransformNode*");
    }

    [Fact]
    public void GetOutput_throws_when_node_is_source()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        NodeDefinition def = MakeDef("FolderInput", new { path = "/tmp" });

        Action act = () => registry.GetOutput(def);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not an IOutputNode*");
    }

    public static TheoryData<string> AllTypeKeysData()
    {
        var data = new TheoryData<string>();
        foreach (string key in AllTypeKeys)
        {
            data.Add(key);
        }
        return data;
    }
}
