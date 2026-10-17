using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.Services;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.UndoRedo.Commands;

namespace FlowForge.UI.ViewModels;

public partial class ConfigFieldViewModel : ViewModelBase
{
    private readonly IDictionary<string, JsonElement> _configDictionary;
    private readonly IDialogService _dialogService;
    private readonly Action<IUndoableCommand>? _onConfigChanged;

    [ObservableProperty]
    private string? _value;

    // Set while the box holds text the config refused; the editor shows it in the error style.
    [ObservableProperty]
    private bool _hasError;

    // While HasError, says what the field takes and what the pipeline still holds; the editor shows it under the box.
    [ObservableProperty]
    private string? _errorText;

    public string Key { get; }
    public string Label { get; }
    public ConfigFieldType FieldType { get; }
    public bool IsRequired { get; }
    public string? Placeholder { get; }
    public IReadOnlyList<string>? Options { get; }
    public string? DefaultValue { get; }
    public string? Tooltip { get; }

    public ConfigFieldViewModel(
        ConfigField field,
        IDictionary<string, JsonElement> configDictionary,
        IDialogService dialogService,
        Action<IUndoableCommand>? onConfigChanged = null)
    {
        _configDictionary = configDictionary;
        _dialogService = dialogService;
        _onConfigChanged = onConfigChanged;
        Key = field.Key;
        Label = field.Label;
        FieldType = field.Type;
        IsRequired = field.Required;
        Placeholder = field.Placeholder;
        Options = field.Options;
        DefaultValue = field.DefaultValue;
        Tooltip = field.Description;

        // Bool must be the capitalized "True"/"False" form BoolStringConverter round-trips;
        // GetRawText yields lowercase, which would re-fire OnValueChanged and loop.
        if (_configDictionary.TryGetValue(Key, out JsonElement element))
        {
            _value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => element.GetRawText(),
            };
        }
        else if (FieldType == ConfigFieldType.Bool && DefaultValue is not null
                 && bool.TryParse(DefaultValue, out bool defaultBool))
        {
            _value = defaultBool ? "True" : "False";
        }
        else
        {
            _value = DefaultValue;
        }
    }

    // The path fields' "..." button. A chosen path goes through Value, so it is one undoable
    // change like typing it; a closed dialog leaves the field as it was.
    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? path = FieldType == ConfigFieldType.FolderPath
            ? await _dialogService.OpenFolderAsync(Label)
            : await _dialogService.OpenFileAsync(Label, "All Files|*.*");
        if (path is not null)
        {
            Value = path;
        }
    }

    partial void OnValueChanged(string? value)
    {
        // An emptied Int box clears the setting; there is no other way to unset a number.
        bool clears = value is null || (FieldType == ConfigFieldType.Int && string.IsNullOrWhiteSpace(value));
        if (!clears)
        {
            // Reject unparseable input so we never serialise a raw string into a Bool/Int slot
            // (GetBoolean/GetInt32 on reload would throw).
            if (FieldType == ConfigFieldType.Int && !TryParseInt(value, out _))
            {
                ErrorText = _configDictionary.TryGetValue(Key, out JsonElement kept)
                    ? $"Whole numbers only. The pipeline keeps {kept.GetRawText()}."
                    : "Whole numbers only. The pipeline keeps no value.";
                HasError = true;
                return;
            }

            if (FieldType == ConfigFieldType.Bool && !bool.TryParse(value, out _))
            {
                HasError = true;
                return;
            }

            HasError = false;
            ErrorText = null;
            bool keyExisted = _configDictionary.TryGetValue(Key, out JsonElement oldElement);

            JsonElement newElement = FieldType switch
            {
                ConfigFieldType.Bool when bool.TryParse(value, out bool b) =>
                    JsonSerializer.SerializeToElement(b),
                ConfigFieldType.Int when TryParseInt(value, out int i) =>
                    JsonSerializer.SerializeToElement(i),
                _ => JsonSerializer.SerializeToElement(value)
            };

            _configDictionary[Key] = newElement;
            _onConfigChanged?.Invoke(new ChangeConfigCommand(
                _configDictionary, Key, oldElement, newElement, keyExisted,
                $"Change {Label}"));
        }
        else
        {
            HasError = false;
            ErrorText = null;
            bool keyExisted = _configDictionary.TryGetValue(Key, out JsonElement oldElement);
            if (!keyExisted)
            {
                return;
            }

            _configDictionary.Remove(Key);
            _onConfigChanged?.Invoke(new ChangeConfigCommand(
                _configDictionary, Key, oldElement, default, keyExisted,
                $"Clear {Label}"));
        }
    }

    // Reads a number the way the user writes it: with their culture's thousands separators, so every
    // keystroke of "1,920" parses and the pipeline never keeps a prefix of it.
    private static bool TryParseInt(string? value, out int number) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.CurrentCulture, out number);
}
