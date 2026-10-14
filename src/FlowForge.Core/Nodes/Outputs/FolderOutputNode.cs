using System.Text.Json;
using FlowForge.Core.Execution;
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

    public DestinationClaim? ClaimDestination(FileJob job, RunContext context)
    {
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

        DestinationClaim claim;
        try
        {
            claim = context.Destinations.Claim(_path, destinationPath, _overwrite, _enableBackup ? _backupSuffix : null);
        }
        catch (IOException ex) when (!ex.Message.StartsWith("FolderOutput: ", StringComparison.Ordinal))
        {
            throw new IOException($"FolderOutput: {ex.Message}", ex);
        }

        if (!string.Equals(claim.Path, destinationPath, StringComparison.Ordinal))
        {
            job.NodeLog.Add($"FolderOutput: '{Path.GetFileName(destinationPath)}' is already delivered in this run; using '{Path.GetFileName(claim.Path)}'");
        }

        PathGuard.EnsureWithinDirectory(claim.Path, _path);
        if (claim.BackupPath is not null)
        {
            PathGuard.EnsureWithinDirectory(claim.BackupPath, _path);
        }

        return claim;
    }

    public async Task ConsumeAsync(FileJob job, DestinationClaim? claim, RunContext context, CancellationToken ct = default)
    {
        if (claim is null)
        {
            throw new InvalidOperationException("FolderOutput: no destination was claimed for this job.");
        }

        ct.ThrowIfCancellationRequested();

        if (context.DryRun)
        {
            if (claim.Kind == ClaimKind.ReplaceWithBackup)
            {
                job.NodeLog.Add($"FolderOutput: Would backup '{claim.Path}' → '{claim.BackupPath}' [dry-run]");
            }

            job.NodeLog.Add($"FolderOutput: → '{claim.Path}' ({_mode}) [dry-run]");
            return;
        }

        string destinationDir = Path.GetDirectoryName(claim.Path) ?? _path;
        Directory.CreateDirectory(destinationDir);
        await DeliverAsync(job, claim, ct).ConfigureAwait(false);

        // The run deletes a moved original only once every delivery of it is in place.
        context.Destinations.RecordDelivery(job.OriginalPath, claim.Path, byMove: string.Equals(_mode, "move", StringComparison.Ordinal));
        job.CurrentPath = claim.Path;
        job.NodeLog.Add($"FolderOutput: → '{claim.Path}' ({_mode})");
    }

    /// <summary>
    /// Copies the job's content to a temporary name beside the destination, then renames it into place,
    /// so a cancelled or failed copy leaves an existing destination untouched.
    /// </summary>
    private async Task DeliverAsync(FileJob job, DestinationClaim claim, CancellationToken ct)
    {
        string destinationPath = claim.Path;
        string tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream sourceStream = new(job.ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using (sourceStream.ConfigureAwait(false))
            {
                FileStream tempStream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using (tempStream.ConfigureAwait(false))
                {
                    await sourceStream.CopyToAsync(tempStream, ct).ConfigureAwait(false);
                }
            }

            // Restore original timestamps on the copy
            var sourceInfo = new FileInfo(job.ContentPath);
            File.SetCreationTimeUtc(tempPath, sourceInfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(tempPath, sourceInfo.LastWriteTimeUtc);

            if (claim.Kind == ClaimKind.ReplaceWithBackup)
            {
                File.Replace(tempPath, destinationPath, claim.BackupPath);
                job.NodeLog.Add($"FolderOutput: Backed up '{destinationPath}' → '{claim.BackupPath}'");
            }
            else
            {
                // A new claim fails on a destination that appeared meanwhile, leaving it intact.
                File.Move(tempPath, destinationPath, overwrite: claim.Kind == ClaimKind.Replace);
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
}
