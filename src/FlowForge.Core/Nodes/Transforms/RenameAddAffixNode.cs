using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Transforms;

public class RenameAddAffixNode : ITransformNode
{
    public string TypeKey => "RenameAddAffix";

    public static IReadOnlyList<ConfigField> ConfigSchema => new[]
    {
        new ConfigField("prefix", ConfigFieldType.String, Label: "Prefix"),
        new ConfigField("suffix", ConfigFieldType.String, Label: "Suffix"),
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

        if (string.IsNullOrEmpty(_prefix) && string.IsNullOrEmpty(_suffix))
        {
            throw new NodeConfigurationException("RenameAddAffix: At least one of 'prefix' or 'suffix' must be provided.");
        }
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

        if (!dryRun && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldPath, newPath, overwrite: false);
        }

        job.CurrentPath = newPath;
        job.NodeLog.Add($"RenameAddAffix: '{oldName}' → '{newFileName}'");

        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }
}
