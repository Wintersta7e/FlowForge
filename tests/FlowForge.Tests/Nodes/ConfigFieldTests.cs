using FlowForge.Core.Nodes.Base;
using FluentAssertions;

namespace FlowForge.Tests.Nodes;

public class ConfigFieldTests
{
    [Fact]
    public void Description_defaults_to_null()
    {
        var field = new ConfigField("key", ConfigFieldType.String, "Label");
        field.Description.Should().BeNull();
    }

    [Fact]
    public void Description_can_be_set()
    {
        var field = new ConfigField("key", ConfigFieldType.String, "Label", Description: "Help text");
        field.Description.Should().Be("Help text");
    }
}
