using System;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.UndoRedo.Commands;

namespace FlowForge.UI.ViewModels;

public partial class ConfigFieldViewModel : ViewModelBase
{
    private readonly IDictionary<string, JsonElement> _configDictionary;
    private readonly Action<IUndoableCommand>? _onConfigChanged;

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
        IDictionary<string, JsonElement> configDictionary,
        Action<IUndoableCommand>? onConfigChanged = null)
    {
        _configDictionary = configDictionary;
        _onConfigChanged = onConfigChanged;
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
        if (value is not null)
        {
            if (FieldType == ConfigFieldType.Int && !int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return;
            }

            // Capture old value before mutation
            bool keyExisted = _configDictionary.TryGetValue(Key, out JsonElement oldElement);

            JsonElement newElement = FieldType switch
            {
                ConfigFieldType.Bool when bool.TryParse(value, out bool b) =>
                    JsonSerializer.SerializeToElement(b),
                ConfigFieldType.Int when int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out int i) =>
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
            // Guard: only create undo entry when key actually existed (I6)
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
}
