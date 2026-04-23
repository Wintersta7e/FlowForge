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

    /// <summary>
    /// Regression guard for the original infinite-loop scenario: after
    /// construction the <see cref="ConfigFieldViewModel.Value"/> must already
    /// match what the CheckBox's BoolStringConverter will write back ("True"
    /// / "False"), so the first binding write-back is a no-op instead of a
    /// fresh PropertyChanged that re-fires OnValueChanged in a loop.
    /// </summary>
    [Fact]
    public void Bool_value_setter_is_idempotent_when_writing_the_current_value()
    {
        var field = new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders");
        var config = new Dictionary<string, JsonElement>
        {
            ["recursive"] = JsonSerializer.SerializeToElement(true),
        };

        var vm = new ConfigFieldViewModel(field, config);
        vm.Value.Should().Be("True", "the VM must arrive pre-normalized or the first binding write-back would re-fire");

        int changeCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(vm.Value), StringComparison.Ordinal))
            {
                changeCount++;
            }
        };

        vm.Value = "True";

        changeCount.Should().Be(0, "setting Value to the current value must be a no-op");
    }

    /// <summary>
    /// A non-parseable bool string must not overwrite the JSON element
    /// with a raw string — GetBoolean on reload would throw.
    /// </summary>
    [Fact]
    public void Bool_field_rejects_unparseable_input_without_mutating_config()
    {
        var field = new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders");
        var config = new Dictionary<string, JsonElement>
        {
            ["recursive"] = JsonSerializer.SerializeToElement(true),
        };

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value = "not-a-bool";

        config["recursive"].ValueKind.Should().Be(JsonValueKind.True,
            "a non-bool string must not overwrite the stored bool — reloads would crash on GetBoolean");
    }
}
