using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());
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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

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

        var vm = new ConfigFieldViewModel(field, config, new StubDialogService());

        vm.Value = "not-an-int";

        config["size"].GetInt32().Should().Be(42,
            "an unparseable int must not overwrite the stored number — reloads would crash on GetInt32");
    }

    /// <summary>
    /// Emptying an Int box clears the setting: the key goes, one undoable command is pushed and
    /// undoing it brings the number back. Text that is not a number never reaches the config and
    /// marks the field until a number is typed.
    /// </summary>
    [Fact]
    public void ClearingIntField_RemovesKeyAndPushesOneUndo()
    {
        Dictionary<string, JsonElement> config = ResizeConfig();
        var received = new List<IUndoableCommand>();
        var vm = new ConfigFieldViewModel(ResizeWidth(), config, new StubDialogService(), received.Add);

        vm.Value = string.Empty;

        config.Should().NotContainKey("width", "an emptied Int box clears the setting");
        received.Should().ContainSingle("one clear is one undo step");
        received[0].Undo();
        config["width"].GetInt32().Should().Be(1920, "undo brings the cleared number back");

        vm.Value = "abc";

        config["width"].GetInt32().Should().Be(1920, "text that is not a number never reaches the config");
        received.Should().ContainSingle("rejected text pushes nothing");
        vm.HasError.Should().BeTrue("the field shows that its text was not taken");

        vm.Value = "640";

        vm.HasError.Should().BeFalse("an accepted number clears the mark");
    }

    /// <summary>
    /// A number typed with the user's thousands separator is stored whole: each keystroke of en-US
    /// "1,920" parses, so the pipeline never ends up holding a prefix of it and no keystroke is refused.
    /// </summary>
    [Fact]
    public void IntField_StoresANumberTypedWithThousandsSeparators()
    {
        Dictionary<string, JsonElement> config = ResizeConfig();
        var vm = new ConfigFieldViewModel(ResizeWidth(), config, new StubDialogService());
        var stored = new List<string>();

        InCulture("en-US", () =>
        {
            foreach (string typed in new[] { "1", "1,", "1,9", "1,92", "1,920" })
            {
                vm.Value = typed;
                stored.Add($"{typed}→{config["width"].GetRawText()} error={vm.HasError}");
            }
        });

        stored.Should().Equal("1→1 error=False", "1,→1 error=False", "1,9→19 error=False", "1,92→192 error=False", "1,920→1920 error=False");
    }

    /// <summary>
    /// Text that is still refused says so in words, naming the number the pipeline keeps; a number
    /// the field takes clears it.
    /// </summary>
    [Fact]
    public void IntField_RefusedText_NamesTheNumberThePipelineKeeps()
    {
        Dictionary<string, JsonElement> config = ResizeConfig();
        var vm = new ConfigFieldViewModel(ResizeWidth(), config, new StubDialogService());

        InCulture("en-US", () =>
        {
            vm.Value = "1";
            vm.Value = "12";
            vm.Value = "12x";
        });

        config["width"].GetInt32().Should().Be(12, "the last number the field took");
        vm.HasError.Should().BeTrue();
        vm.ErrorText.Should().Be("Whole numbers only. The pipeline keeps 12.");

        vm.Value = "120";

        vm.ErrorText.Should().BeNull("a number the field takes clears the message");
    }

    /// <summary>
    /// Redo of a clear removes the key again rather than leaving an empty value in its place.
    /// </summary>
    [Fact]
    public void RedoOfClear_RemovesKeyAgain()
    {
        Dictionary<string, JsonElement> config = ResizeConfig();
        var received = new List<IUndoableCommand>();
        var vm = new ConfigFieldViewModel(ResizeWidth(), config, new StubDialogService(), received.Add);
        vm.Value = null;
        IUndoableCommand clear = received.Single();

        clear.Undo();
        clear.Execute();

        string found = config.TryGetValue("width", out JsonElement left) ? $"ValueKind = {left.ValueKind}" : "absent";
        found.Should().Be("absent", "redo of a clear removes the key again");
    }

    /// <summary>
    /// A folder field's browse button asks the dialog service for a folder; the chosen folder is
    /// stored as one undoable change.
    /// </summary>
    [Fact]
    public async Task Browse_FolderField_UsesDialogAndPushesOneChange()
    {
        using var temp = new TempDirectory();
        var dialog = new StubDialogService { OpenFolderResult = temp.Path };
        var config = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var received = new List<IUndoableCommand>();
        var vm = new ConfigFieldViewModel(SourceFolder(), config, dialog, received.Add);

        await vm.BrowseCommand.ExecuteAsync(null);

        dialog.Calls.Should().Equal([nameof(StubDialogService.OpenFolderAsync)], "a folder field asks for a folder");
        config.Should().ContainKey("path", "the chosen folder lands in the config");
        config["path"].GetString().Should().Be(temp.Path);
        received.Should().ContainSingle("one pick is one undo step");
        received[0].Undo();
        config.Should().NotContainKey("path", "undo removes the picked folder");
    }

    /// <summary>
    /// A file field asks for a file; a dialog closed without a choice leaves the field, its
    /// config and the history as they were.
    /// </summary>
    [Fact]
    public async Task Browse_Cancelled_ChangesNothing()
    {
        var field = new ConfigField("list", ConfigFieldType.FilePath, Label: "List File");
        Dictionary<string, JsonElement> config = ConfigHelper.MakeConfig(("list", "kept.txt"));
        var dialog = new StubDialogService();
        var received = new List<IUndoableCommand>();
        var vm = new ConfigFieldViewModel(field, config, dialog, received.Add);

        await vm.BrowseCommand.ExecuteAsync(null);

        dialog.Calls.Should().Equal([nameof(StubDialogService.OpenFileAsync)], "a file field asks for a file");
        vm.Value.Should().Be("kept.txt");
        config["list"].GetString().Should().Be("kept.txt", "a cancelled dialog changes nothing");
        received.Should().BeEmpty("a cancelled dialog pushes no undo step");
    }

    private static ConfigField SourceFolder() =>
        NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetConfigSchema("FolderInput")
            .Single(field => string.Equals(field.Key, "path", StringComparison.Ordinal));

    private static ConfigField ResizeWidth() =>
        NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetConfigSchema("ImageResize")
            .Single(field => string.Equals(field.Key, "width", StringComparison.Ordinal));

    private static Dictionary<string, JsonElement> ResizeConfig() =>
        new(StringComparer.Ordinal) { ["width"] = JsonSerializer.SerializeToElement(1920) };

    // Runs the edits in a fixed culture, so the separators they type mean the same on every machine.
    private static void InCulture(string name, Action edits)
    {
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
        try
        {
            edits();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }
}
