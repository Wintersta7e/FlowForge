using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Outputs;

public class FolderOutputNode : IOutputNode
{
    public string TypeKey => "FolderOutput";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("path", ConfigFieldType.FolderPath, Label: "Output Folder", Required: true),
        new ConfigField("mode", ConfigFieldType.Select, Label: "Mode", DefaultValue: "copy",
            Options: new[] { "copy", "move" }),
        new ConfigField("overwrite", ConfigFieldType.Bool, Label: "Overwrite Existing", DefaultValue: "false"),
        new ConfigField("preserveStructure", ConfigFieldType.Bool, Label: "Preserve Folder Structure", DefaultValue: "false"),
        new ConfigField("sourceBasePath", ConfigFieldType.FolderPath, Label: "Source Base Path"),
    };

    private string _path = string.Empty;
    private string _mode = "copy";
    private bool _overwrite;
    private bool _preserveStructure;
    private string _sourceBasePath = string.Empty;

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("path", out JsonElement pathElement) ||
            pathElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("FolderOutput: 'path' is required.");
        }

        _path = pathElement.GetString()
            ?? throw new NodeConfigurationException("FolderOutput: 'path' must be a non-null string.");

        if (config.TryGetValue("mode", out JsonElement modeElement))
        {
            _mode = modeElement.GetString() ?? "copy";
        }

        if (_mode is not "copy" and not "move")
        {
            throw new NodeConfigurationException($"FolderOutput: Unknown mode '{_mode}'. Must be 'copy' or 'move'.");
        }

        if (config.TryGetValue("overwrite", out JsonElement overwriteElement))
        {
            _overwrite = overwriteElement.GetBoolean();
        }

        if (config.TryGetValue("preserveStructure", out JsonElement preserveElement))
        {
            _preserveStructure = preserveElement.GetBoolean();
        }

        if (config.TryGetValue("sourceBasePath", out JsonElement basePathElement))
        {
            _sourceBasePath = basePathElement.GetString() ?? string.Empty;
        }
    }

    public async Task ConsumeAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string fileName = Path.GetFileName(job.CurrentPath);
        string destinationDir = _path;

        if (_preserveStructure && !string.IsNullOrEmpty(_sourceBasePath))
        {
            string? originalDir = Path.GetDirectoryName(job.OriginalPath);
            if (originalDir != null && originalDir.StartsWith(_sourceBasePath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = Path.GetRelativePath(_sourceBasePath, originalDir);
                destinationDir = Path.Combine(_path, relativePath);
            }
        }

        string destinationPath = Path.Combine(destinationDir, fileName);

        if (dryRun)
        {
            job.NodeLog.Add($"FolderOutput: → '{destinationPath}' ({_mode}) [dry-run]");
            return;
        }

        Directory.CreateDirectory(destinationDir);

        if (_mode.Equals("move", StringComparison.OrdinalIgnoreCase))
        {
            // .NET has no async File.Move; sync is acceptable for metadata-only renames
            File.Move(job.CurrentPath, destinationPath, overwrite: _overwrite);
        }
        else
        {
            // Use async streams for copy to avoid blocking the thread pool on large files
            FileMode destMode = _overwrite ? FileMode.Create : FileMode.CreateNew;
            await using FileStream sourceStream = new(job.CurrentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using FileStream destStream = new(destinationPath, destMode, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await sourceStream.CopyToAsync(destStream, ct);

            // Restore original timestamps on the copy
            var sourceInfo = new FileInfo(job.CurrentPath);
            File.SetCreationTimeUtc(destinationPath, sourceInfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(destinationPath, sourceInfo.LastWriteTimeUtc);
        }

        job.CurrentPath = destinationPath;
        job.NodeLog.Add($"FolderOutput: → '{destinationPath}' ({_mode})");
    }
}
