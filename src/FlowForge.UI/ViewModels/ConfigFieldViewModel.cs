using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.UI.ViewModels;

public partial class ConfigFieldViewModel : ViewModelBase
{
    private readonly Dictionary<string, JsonElement> _configDictionary;

    [ObservableProperty]
    private string? _value;

    public string Key { get; }
    public string Label { get; }
    public ConfigFieldType FieldType { get; }
    public bool IsRequired { get; }
    public string? Placeholder { get; }
    public IReadOnlyList<string>? Options { get; }
    public string? DefaultValue { get; }

    public ConfigFieldViewModel(
        ConfigField field,
        Dictionary<string, JsonElement> configDictionary)
    {
        _configDictionary = configDictionary;
        Key = field.Key;
        Label = field.Label;
        FieldType = field.Type;
        IsRequired = field.Required;
        Placeholder = field.Placeholder;
        Options = field.Options;
        DefaultValue = field.DefaultValue;

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
            _configDictionary[Key] = JsonSerializer.SerializeToElement(value);
        }
        else
        {
            _configDictionary.Remove(Key);
        }
    }
}
