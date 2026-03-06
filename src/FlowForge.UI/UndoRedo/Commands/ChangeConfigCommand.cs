using System.Collections.Generic;
using System.Text.Json;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class ChangeConfigCommand : IUndoableCommand
{
    private readonly Dictionary<string, JsonElement> _config;
    private readonly string _key;
    private readonly JsonElement _oldValue;
    private readonly JsonElement _newValue;
    private readonly bool _keyExisted;

    public string Description { get; }

    /// <summary>
    /// Identifies the config field for coalescing repeated edits (e.g., keystrokes).
    /// </summary>
    public string ConfigKey => _key;

    public ChangeConfigCommand(
        Dictionary<string, JsonElement> config,
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
        _config[_key] = _newValue;
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
