using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class MwOpsMapTests
{
    /// <summary>
    /// Every TypeKey registered in the default NodeRegistry must have a
    /// bespoke MwOpsMap entry. The fallback returns the generic "Cog" icon
    /// and the "hea" category bucket — landing a source or output node
    /// there gives the wrong aura / port glow / neon accent colour.
    /// </summary>
    [Fact]
    public void Every_registered_type_key_has_a_bespoke_entry()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);

        foreach (string typeKey in registry.GetRegisteredTypeKeys())
        {
            MwOpsMap.OpMeta meta = MwOpsMap.Get(typeKey);
            meta.Icon.Should().NotBe("Cog", $"{typeKey} must map to a bespoke Molten Works icon, not the Cog fallback");
            meta.Sub.Should().NotBeEmpty($"{typeKey} must map to a bespoke Molten Works subtitle");
            meta.Code.Should().NotBeEmpty($"{typeKey} must map to a bespoke Molten Works code");
        }
    }

    /// <summary>
    /// The MwOpsMap category bucket must be consistent with the node's
    /// runtime NodeCategory: Source → "src", Output → "snk", Transform →
    /// one of the transform buckets. Without this the "bespoke entry"
    /// test above still passes if someone fat-fingers a source node into
    /// "hea", producing a silent visual regression.
    /// </summary>
    [Fact]
    public void OpMeta_category_is_consistent_with_registry_node_category()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);

        foreach (string typeKey in registry.GetRegisteredTypeKeys())
        {
            NodeCategory runtimeCategory = registry.GetCategoryForTypeKey(typeKey);
            string bucket = MwOpsMap.Get(typeKey).Category;

            bool compatible = runtimeCategory switch
            {
                NodeCategory.Source => string.Equals(bucket, "src", System.StringComparison.Ordinal),
                NodeCategory.Output => string.Equals(bucket, "snk", System.StringComparison.Ordinal),
                NodeCategory.Transform => bucket is "shp" or "flt" or "hea" or "met",
                _ => false,
            };

            compatible.Should().BeTrue(
                $"{typeKey} has NodeCategory.{runtimeCategory} but MwOpsMap bucket '{bucket}' — mismatch produces wrong aura/port colours");
        }
    }
}
