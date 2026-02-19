using System.Runtime.CompilerServices;
using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Sources;

public class FolderInputNode : ISourceNode
{
    public string TypeKey => "FolderInput";

    public static IReadOnlyList<ConfigField> ConfigSchema => new[]
    {
        new ConfigField("path", ConfigFieldType.FolderPath, Label: "Source Folder", Required: true),
        new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders", DefaultValue: "false"),
        new ConfigField("filter", ConfigFieldType.String, Label: "File Filter", DefaultValue: "*", Placeholder: "*.jpg;*.png"),
    };

    private string _path = string.Empty;
    private bool _recursive;
    private string _filter = "*";

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("path", out JsonElement pathElement) ||
            pathElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("FolderInput: 'path' is required.");
        }

        _path = pathElement.GetString()
            ?? throw new NodeConfigurationException("FolderInput: 'path' must be a non-null string.");

        if (config.TryGetValue("recursive", out JsonElement recursiveElement))
        {
            _recursive = recursiveElement.GetBoolean();
        }

        if (config.TryGetValue("filter", out JsonElement filterElement))
        {
            string filterValue = filterElement.GetString() ?? "*";
            if (!string.IsNullOrWhiteSpace(filterValue))
            {
                _filter = filterValue;
            }
        }
    }

    public async IAsyncEnumerable<FileJob> ProduceAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_path))
        {
            throw new DirectoryNotFoundException($"Source folder not found: '{_path}'");
        }

        SearchOption searchOption = _recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] patterns = _filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var files = new List<string>();
        foreach (string pattern in patterns)
        {
            IEnumerable<string> matched = Directory.EnumerateFiles(_path, pattern, searchOption);
            files.AddRange(matched);
        }

        // Deduplicate (multiple patterns could match the same file) and sort for deterministic order
        IEnumerable<string> uniqueFiles = files.Distinct().OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (string filePath in uniqueFiles)
        {
            ct.ThrowIfCancellationRequested();

            yield return new FileJob
            {
                OriginalPath = filePath,
                CurrentPath = filePath
            };
        }

        await Task.CompletedTask; // Satisfy async requirement
    }
}
