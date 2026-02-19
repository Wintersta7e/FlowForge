namespace FlowForge.Core.Nodes.Base;

/// <summary>
/// Defines the type of UI control to render for a node configuration field.
/// </summary>
public enum ConfigFieldType
{
    String,
    Int,
    Bool,
    FilePath,
    FolderPath,
    Select,
    MultiLine
}

/// <summary>
/// Describes a single configurable property on a node, enabling the UI
/// to auto-generate property forms without hard-coding per-node knowledge.
/// </summary>
/// <param name="Key">The config dictionary key (matches what Configure() reads).</param>
/// <param name="Type">Determines the UI control type.</param>
/// <param name="Label">Human-readable display label.</param>
/// <param name="Required">Whether the field must have a value.</param>
/// <param name="DefaultValue">Default value shown in the UI (null = none).</param>
/// <param name="Placeholder">Hint text shown when the field is empty.</param>
/// <param name="Options">Dropdown options for <see cref="ConfigFieldType.Select"/> fields.</param>
public sealed record ConfigField(
    string Key,
    ConfigFieldType Type,
    string Label,
    bool Required = false,
    string? DefaultValue = null,
    string? Placeholder = null,
    IReadOnlyList<string>? Options = null);
