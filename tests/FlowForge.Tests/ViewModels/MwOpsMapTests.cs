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
    /// and forces the node into the "hea" category bucket — a renamed or
    /// newly added TypeKey would silently land there with the wrong aura,
    /// subtitle, and code label, and nothing would crash to flag the gap.
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
}
