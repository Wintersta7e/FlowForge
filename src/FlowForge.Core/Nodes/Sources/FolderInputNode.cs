using System.Runtime.CompilerServices;
using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Serilog;

namespace FlowForge.Core.Nodes.Sources;

public class FolderInputNode : ISourceNode
{
    public string TypeKey => "FolderInput";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("path", ConfigFieldType.FolderPath, Label: "Source Folder", Required: true, Description: "Root folder to scan for files"),
        new ConfigField("recursive", ConfigFieldType.Bool, Label: "Include Subfolders", DefaultValue: "false", Description: "Search subdirectories recursively"),
        new ConfigField("filter", ConfigFieldType.String, Label: "File Filter", DefaultValue: "*", Placeholder: "*.jpg;*.png", Description: "Semicolon-separated glob patterns (e.g. *.jpg;*.png)"),
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

        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pattern in patterns)
        {
            try
            {
                foreach (string file in Directory.EnumerateFiles(_path, pattern, searchOption))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warning(ex, "FolderInput: access denied enumerating '{Path}' with pattern '{Pattern}', falling back to top-level", _path, pattern);
                if (searchOption == SearchOption.AllDirectories)
                {
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(_path, pattern, SearchOption.TopDirectoryOnly))
                        {
                            files.Add(file);
                        }
                    }
                    catch (UnauthorizedAccessException ex2)
                    {
                        Log.Warning(ex2, "FolderInput: access denied even at top-level for '{Path}' with pattern '{Pattern}'", _path, pattern);
                    }
                }
            }
        }

        // SortedSet handles dedup and ordering automatically

        foreach (string filePath in files)
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
