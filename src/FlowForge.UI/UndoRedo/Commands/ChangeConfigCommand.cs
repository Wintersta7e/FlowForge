using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class ChangeConfigCommand : IUndoableCommand
{
    private readonly IDictionary<string, JsonElement> _config;
    private readonly string _key;
    private readonly JsonElement _oldValue;
    private readonly JsonElement _newValue;
    private readonly bool _keyExisted;

    public string Description { get; }

    public ChangeConfigCommand(
        IDictionary<string, JsonElement> config,
        string key,
        JsonElement oldValue,
        JsonElement newValue,
        bool keyExisted,
        string description)
    {
        _config = config;
        _key = key;
        _oldValue = oldValue;
        _newValue = newValue;
        _keyExisted = keyExisted;
        Description = description;
    }

    public void Execute()
    {
        // A clear carries no new value: redoing it removes the key again.
        if (_newValue.ValueKind == JsonValueKind.Undefined)
        {
            _config.Remove(_key);
        }
        else
        {
            _config[_key] = _newValue;
        }
    }

    /// <summary>
    /// One command doing this edit and then <paramref name="next"/>, or null when
    /// <paramref name="next"/> edits another field. It undoes to the value from before this edit.
    /// </summary>
    public ChangeConfigCommand? TryMerge(ChangeConfigCommand next)
    {
        if (!ReferenceEquals(_config, next._config) || !string.Equals(_key, next._key, StringComparison.Ordinal))
        {
            return null;
        }

        return new ChangeConfigCommand(_config, _key, _oldValue, next._newValue, _keyExisted, next.Description);
    }

    public void Undo()
    {
        if (_keyExisted)
        {
            _config[_key] = _oldValue;
        }
        else
        {
            _config.Remove(_key);
        }
    }
}
