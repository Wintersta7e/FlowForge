using System.Collections.Generic;
using System.Text.Json;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class ChangeConfigCommand : IUndoableCommand
{
    private readonly Dictionary<string, JsonElement> _config;
    private readonly string _key;
    private readonly JsonElement _oldValue;
    private readonly JsonElement _newValue;

    public string Description { get; }

    public ChangeConfigCommand(
        Dictionary<string, JsonElement> config,
        string key,
        JsonElement oldValue,
        JsonElement newValue,
        string description)
    {
        _config = config;
        _key = key;
        _oldValue = oldValue;
        _newValue = newValue;
        Description = description;
    }

    public void Execute() => _config[_key] = _newValue;

    public void Undo() => _config[_key] = _oldValue;
}
