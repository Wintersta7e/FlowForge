using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Sources;

public class FolderInputNode : ISourceNode
{
    private static readonly IComparer<string> DiscoveryOrder = Comparer<string>.Create(static (a, b) =>
    {
        int byIgnoreCase = StringComparer.OrdinalIgnoreCase.Compare(a, b);
        return byIgnoreCase != 0 ? byIgnoreCase : StringComparer.Ordinal.Compare(a, b);
    });

    private readonly ILogger<FolderInputNode> _logger;

    public FolderInputNode(ILogger<FolderInputNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

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

    public void Configure(IDictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("path", out JsonElement pathElement) ||
            pathElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("FolderInput: 'path' is required.");
        }

        _path = pathElement.GetString()
            ?? throw new NodeConfigurationException("FolderInput: 'path' must be a non-null string.");

        if (string.IsNullOrWhiteSpace(_path))
        {
            throw new NodeConfigurationException("FolderInput: 'path' is empty — set a source folder on the Source station.");
        }

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

        string[] patterns = _filter.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_path));
        var files = new SortedSet<string>(DiscoveryOrder);
        foreach (string pattern in patterns)
        {
            foreach (string file in EnumerateMatches(pattern))
            {
                // Filter out files that resolve outside the source root (e.g. a pattern starting with "..").
                string resolvedFile = Path.GetFullPath(file);
                if (!PathGuard.IsWithinResolvedDirectory(resolvedFile, resolvedRoot))
                {
                    _logger.LogWarning(
                        "FolderInput: skipping '{FilePath}' — resolves outside source root '{Root}'",
                        file,
                        resolvedRoot);
                    continue;
                }

                files.Add(file);
            }
        }

        foreach (string filePath in files)
        {
            ct.ThrowIfCancellationRequested();

            yield return new FileJob
            {
                OriginalPath = filePath,
                CurrentPath = filePath
            };
        }

        await Task.CompletedTask.ConfigureAwait(false); // Satisfy async requirement
    }

    /// <summary>
    /// Lists the files one pattern matches as <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
    /// does, with the platform's casing, except that links are never yielded, linked folders are never
    /// scanned, hidden and system files are kept, and an unreadable folder is skipped.
    /// </summary>
    private FileSystemEnumerable<string> EnumerateMatches(string pattern)
    {
        string? patternDirectory = Path.GetDirectoryName(pattern);
        string directory = string.IsNullOrEmpty(patternDirectory) ? _path : Path.Join(_path, patternDirectory);
        string namePattern = Path.GetFileName(pattern);
        string expression = namePattern is "" or "." or "*.*" ? "*" : FileSystemName.TranslateWin32Expression(namePattern);
        bool ignoreCase = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = _recursive,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = true,
        };

        return new FileSystemEnumerable<string>(directory, static (ref FileSystemEntry e) => e.ToSpecifiedFullPath(), options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory
                && FileSystemName.MatchesWin32Expression(expression, e.FileName, ignoreCase)
                && !PathGuard.IsLink(e.ToFileSystemInfo()),
            ShouldRecursePredicate = static (ref FileSystemEntry e) => !PathGuard.IsLink(e.ToFileSystemInfo()),
        };
    }
}
