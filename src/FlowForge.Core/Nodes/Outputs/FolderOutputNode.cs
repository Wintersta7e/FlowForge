using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Outputs;

public class FolderOutputNode : IOutputNode
{
    private readonly ILogger<FolderOutputNode> _logger;

    public FolderOutputNode(ILogger<FolderOutputNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "FolderOutput";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("path", ConfigFieldType.FolderPath, Label: "Output Folder", Required: true, Description: "Destination folder for processed files"),
        new ConfigField("mode", ConfigFieldType.Select, Label: "Mode", DefaultValue: "copy",
            Options: new[] { "copy", "move" }, Description: "copy: leave each file in the input folder (rename and image nodes have already changed it there), move: delete it after transfer"),
        new ConfigField("overwrite", ConfigFieldType.Bool, Label: "Overwrite Existing", DefaultValue: "false", Description: "Replace existing files at destination"),
        new ConfigField("preserveStructure", ConfigFieldType.Bool, Label: "Preserve Folder Structure", DefaultValue: "false", Description: "Recreate source folder hierarchy in output"),
        new ConfigField("sourceBasePath", ConfigFieldType.FolderPath, Label: "Source Base Path", Description: "Root path for computing relative subdirectories"),
        new ConfigField("enableBackup", ConfigFieldType.Bool, Label: "Backup Before Overwrite", DefaultValue: "false",
            Description: "Create a backup copy of existing destination files before overwriting"),
        new ConfigField("backupSuffix", ConfigFieldType.String, Label: "Backup Suffix", DefaultValue: ".bak",
            Description: "File extension appended to backup copies (e.g. .bak, .orig)"),
    };

    private string _path = string.Empty;
    private string _mode = "copy";
    private bool _overwrite;
    private bool _preserveStructure;
    private string _sourceBasePath = string.Empty;
    private bool _enableBackup;
    private string _backupSuffix = ".bak";

    public void Configure(IDictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("path", out JsonElement pathElement) ||
            pathElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("FolderOutput: 'path' is required.");
        }

        _path = pathElement.GetString()
            ?? throw new NodeConfigurationException("FolderOutput: 'path' must be a non-null string.");

        if (string.IsNullOrWhiteSpace(_path))
        {
            throw new NodeConfigurationException("FolderOutput: 'path' is empty — set a destination folder on the Destination station.");
        }

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

        if (config.TryGetValue("enableBackup", out JsonElement backupElement))
        {
            _enableBackup = backupElement.GetBoolean();
        }

        _logger.LogDebug("FolderOutput: configured with Path={Path}, Mode={Mode}, Overwrite={Overwrite}, PreserveStructure={PreserveStructure}, EnableBackup={EnableBackup}",
            _path, _mode, _overwrite, _preserveStructure, _enableBackup);

        if (config.TryGetValue("backupSuffix", out JsonElement suffixElement))
        {
            string suffix = suffixElement.GetString() ?? ".bak";
            if (suffix.Length < 2 ||
                !suffix.StartsWith('.') ||
                suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                suffix.Contains('/') ||
                suffix.Contains('\\'))
            {
                throw new NodeConfigurationException(
                    $"FolderOutput: 'backupSuffix' must be a simple file extension (e.g. '.bak'). Got: '{suffix}'");
            }

            _backupSuffix = suffix;
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
            if (originalDir != null)
            {
                // A file outside the base folder is delivered flat.
                string resolvedDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originalDir));
                string resolvedBase = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_sourceBasePath));
                if (PathGuard.IsWithinResolvedDirectory(resolvedDir, resolvedBase))
                {
                    destinationDir = Path.Combine(_path, Path.GetRelativePath(resolvedBase, resolvedDir));
                }
            }
        }

        string destinationPath = Path.Combine(destinationDir, fileName);

        PathGuard.EnsureWithinDirectory(destinationPath, _path);
        RefuseLinkedComponents(destinationPath);

        if (dryRun)
        {
            if (_enableBackup && _overwrite && File.Exists(destinationPath))
            {
                job.NodeLog.Add($"FolderOutput: Would backup '{destinationPath}' → '{destinationPath}{_backupSuffix}' [dry-run]");
            }

            job.NodeLog.Add($"FolderOutput: → '{destinationPath}' ({_mode}) [dry-run]");
            return;
        }

        Directory.CreateDirectory(destinationDir);
        await DeliverAsync(job, destinationPath, ct).ConfigureAwait(false);

        // Delete the path the delivered bytes came from, never the file just delivered.
        if (_mode.Equals("move", StringComparison.OrdinalIgnoreCase) && !PathGuard.IsSameFile(job.CurrentPath, destinationPath))
        {
            DeleteMovedSource(job);
        }

        job.CurrentPath = destinationPath;
        job.NodeLog.Add($"FolderOutput: → '{destinationPath}' ({_mode})");
    }

    /// <summary>
    /// Refuses the destination when it, or any existing folder between the output folder and it, is a
    /// link: a folder or file created under a linked folder lands outside the output folder. The output
    /// folder itself may be a link. Only attributes are read, so a dry run reports the same refusal.
    /// </summary>
    private void RefuseLinkedComponents(string destinationPath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_path));
        string current = root;
        foreach (string component in Path.GetRelativePath(root, Path.GetFullPath(destinationPath)).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (!RefuseLink(current))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Throws when the entry at <paramref name="path"/> is a link; returns whether an entry exists there.
    /// </summary>
    private static bool RefuseLink(string path)
    {
        var directory = new DirectoryInfo(path);
        FileSystemInfo entry = directory.Exists ? directory : new FileInfo(path);
        if (entry.Exists && PathGuard.IsLink(entry))
        {
            throw new InvalidOperationException($"FolderOutput: '{path}' is a link; refusing to deliver through it.");
        }

        return entry.Exists;
    }

    /// <summary>
    /// Copies the job's file to a temporary name beside the destination, then renames it into place,
    /// so a cancelled or failed copy leaves an existing destination untouched.
    /// </summary>
    private async Task DeliverAsync(FileJob job, string destinationPath, CancellationToken ct)
    {
        string tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream sourceStream = new(job.CurrentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using (sourceStream.ConfigureAwait(false))
            {
                FileStream tempStream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using (tempStream.ConfigureAwait(false))
                {
                    await sourceStream.CopyToAsync(tempStream, ct).ConfigureAwait(false);
                }
            }

            // Restore original timestamps on the copy
            var sourceInfo = new FileInfo(job.CurrentPath);
            File.SetCreationTimeUtc(tempPath, sourceInfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(tempPath, sourceInfo.LastWriteTimeUtc);

            bool exists = File.Exists(destinationPath);
            if (exists && _overwrite && _enableBackup)
            {
                string backupPath = destinationPath + _backupSuffix;
                PathGuard.EnsureWithinDirectory(backupPath, _path);
                File.Replace(tempPath, destinationPath, backupPath);
                job.NodeLog.Add($"FolderOutput: Backed up '{destinationPath}' → '{backupPath}'");
            }
            else
            {
                // Without overwrite this fails on a destination that appeared meanwhile, leaving it intact.
                File.Move(tempPath, destinationPath, overwrite: exists && _overwrite);
            }
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "FolderOutput: could not delete temporary file {TempPath}", tempPath);
            }
        }
    }

    private void DeleteMovedSource(FileJob job)
    {
        try
        {
            File.Delete(job.CurrentPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "FolderOutput: delivered but could not delete source {Path}", job.CurrentPath);
            job.NodeLog.Add($"FolderOutput: Delivered, but could not delete the source '{job.CurrentPath}': {ex.Message}");
        }
    }
}
