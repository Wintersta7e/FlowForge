using System;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.UI.ViewModels;

public partial class ConfigFieldViewModel : ViewModelBase
{
    private readonly Dictionary<string, JsonElement> _configDictionary;
    private readonly Action? _onValueChanged;

    [ObservableProperty]
    private string? _value;

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
        Dictionary<string, JsonElement> configDictionary,
        Action? onValueChanged = null)
    {
        _configDictionary = configDictionary;
        _onValueChanged = onValueChanged;
        Key = field.Key;
        Label = field.Label;
        FieldType = field.Type;
        IsRequired = field.Required;
        Placeholder = field.Placeholder;
        Options = field.Options;
        DefaultValue = field.DefaultValue;
        Tooltip = field.Description;

        // Read initial value from config dictionary
        if (_configDictionary.TryGetValue(Key, out JsonElement element))
        {
            _value = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.GetRawText();
        }
        else
        {
            _value = DefaultValue;
        }
    }

    partial void OnValueChanged(string? value)
    {
        // Write back to config dictionary
        if (value is not null)
        {
            // For Int fields, only update config if the value is a valid integer;
            // skip update for partial/invalid input to keep the last valid value
            if (FieldType == ConfigFieldType.Int && !int.TryParse(value, out _))
            {
                return;
            }

            _configDictionary[Key] = FieldType switch
            {
                ConfigFieldType.Bool when bool.TryParse(value, out bool b) =>
                    JsonSerializer.SerializeToElement(b),
                ConfigFieldType.Int when int.TryParse(value, out int i) =>
                    JsonSerializer.SerializeToElement(i),
                _ => JsonSerializer.SerializeToElement(value)
            };
        }
        else
        {
            _configDictionary.Remove(Key);
        }

        _onValueChanged?.Invoke();
    }
}
