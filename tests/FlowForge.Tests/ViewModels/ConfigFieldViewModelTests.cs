using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.ViewModels;

namespace FlowForge.Tests.ViewModels;

public class ConfigFieldViewModelTests
{
    /// <summary>
    /// CheckBox's BoolStringConverter emits capitalized "True"/"False"; the
    /// initial Value must match so the first binding write-back is a no-op.
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
    /// Setting Value to the current value must be a PropertyChanged no-op so
    /// a binding write-back cannot re-enter OnValueChanged.
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

    /// <summary>
    /// Symmetric contract with the Bool guard: an unparseable int string must
    /// not land in a ConfigFieldType.Int slot, or GetInt32 on reload throws.
    /// </summary>
    [Fact]
    public void Int_field_rejects_unparseable_input_without_mutating_config()
    {
        var field = new ConfigField("size", ConfigFieldType.Int, Label: "Size");
        var config = new Dictionary<string, JsonElement>
        {
            ["size"] = JsonSerializer.SerializeToElement(42),
        };

        var vm = new ConfigFieldViewModel(field, config);

        vm.Value = "not-an-int";

        config["size"].GetInt32().Should().Be(42,
            "an unparseable int must not overwrite the stored number — reloads would crash on GetInt32");
    }
}
