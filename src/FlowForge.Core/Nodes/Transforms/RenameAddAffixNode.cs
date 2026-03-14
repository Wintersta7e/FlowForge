using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Transforms;

public class RenameAddAffixNode : ITransformNode
{
    private readonly ILogger<RenameAddAffixNode> _logger;

    public RenameAddAffixNode(ILogger<RenameAddAffixNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "RenameAddAffix";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("prefix", ConfigFieldType.String, Label: "Prefix", Description: "Text to prepend before filename"),
        new ConfigField("suffix", ConfigFieldType.String, Label: "Suffix", Description: "Text to append after filename (before extension)"),
    };

    private string _prefix = string.Empty;
    private string _suffix = string.Empty;

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (config.TryGetValue("prefix", out JsonElement prefixElement) &&
            prefixElement.ValueKind == JsonValueKind.String)
        {
            _prefix = prefixElement.GetString() ?? string.Empty;
        }

        if (config.TryGetValue("suffix", out JsonElement suffixElement) &&
            suffixElement.ValueKind == JsonValueKind.String)
        {
            _suffix = suffixElement.GetString() ?? string.Empty;
        }

        if (_prefix.Contains(Path.DirectorySeparatorChar) || _prefix.Contains(Path.AltDirectorySeparatorChar) || _prefix.Contains(".."))
        {
            throw new NodeConfigurationException("RenameAddAffix: 'prefix' must not contain path separators or '..' sequences.");
        }

        if (_suffix.Contains(Path.DirectorySeparatorChar) || _suffix.Contains(Path.AltDirectorySeparatorChar) || _suffix.Contains(".."))
        {
            throw new NodeConfigurationException("RenameAddAffix: 'suffix' must not contain path separators or '..' sequences.");
        }

        if (string.IsNullOrEmpty(_prefix) && string.IsNullOrEmpty(_suffix))
        {
            throw new NodeConfigurationException("RenameAddAffix: At least one of 'prefix' or 'suffix' must be provided.");
        }

        _logger.LogDebug("RenameAddAffix: configured with Prefix={Prefix}, Suffix={Suffix}", _prefix, _suffix);
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string oldName = job.FileName;
        string directory = job.DirectoryName;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(job.CurrentPath);
        string extension = Path.GetExtension(job.CurrentPath);

        string newFileName = $"{_prefix}{nameWithoutExt}{_suffix}{extension}";
        string oldPath = job.CurrentPath;
        string newPath = Path.Combine(directory, newFileName);

        PathGuard.EnsureWithinDirectory(newPath, directory);

        if (!dryRun && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            // .NET has no async File.Move/Copy API; sync call is acceptable for metadata-only operations
            File.Move(oldPath, newPath, overwrite: false);
        }

        job.CurrentPath = newPath;
        job.NodeLog.Add($"RenameAddAffix: '{oldName}' → '{newFileName}'");

        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }
}
