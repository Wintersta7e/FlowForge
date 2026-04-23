using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.ViewModels;

namespace FlowForge.Tests.ViewModels;

public class ConfigFieldViewModelTests
{
    /// <summary>
    /// Regression: a bool stored in the config dict via JsonSerializer.SerializeToElement
    /// round-trips through JsonElement.GetRawText() as lowercase "true"/"false". The
    /// BoolStringConverter used by the Bool editor emits capitalized "True"/"False". If
    /// the initial _value is lowercase, the first binding write-back sees a different
    /// string and re-fires OnValueChanged — which previously caused an infinite
    /// refresh loop that froze the UI thread after toggling a CheckBox.
    /// </summary>
    [Theory]
    [InlineData(true, "True")]
    [InlineData(false, "False")]
    public void Bool_value_from_json_element_normalized_to_capitalized_string(bool input, string expected)
    {
        var field = new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders");
        var config = new Dictionary<string, JsonElement>
        {
            ["recursive"] = JsonSerializer.SerializeToElement(input),
        };

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("true", "True")]
    [InlineData("True", "True")]
    [InlineData("false", "False")]
    [InlineData("FALSE", "False")]
    public void Bool_default_value_normalized_to_capitalized_string(string defaultText, string expected)
    {
        var field = new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders", DefaultValue: defaultText);
        var config = new Dictionary<string, JsonElement>();

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value.Should().Be(expected);
    }

    [Fact]
    public void String_value_passes_through_unchanged()
    {
        var field = new ConfigField("path", ConfigFieldType.String, Label: "Path");
        var config = new Dictionary<string, JsonElement>
        {
            ["path"] = JsonSerializer.SerializeToElement("C:\\foo\\bar"),
        };

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value.Should().Be("C:\\foo\\bar");
    }

    [Fact]
    public void Int_value_passes_through_as_raw_text()
    {
        var field = new ConfigField("size", ConfigFieldType.Int, Label: "Size");
        var config = new Dictionary<string, JsonElement>
        {
            ["size"] = JsonSerializer.SerializeToElement(42),
        };

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value.Should().Be("42");
    }
}
