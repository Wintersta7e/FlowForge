using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Transforms;

public class RenameRegexNode : ITransformNode
{
    public string TypeKey => "RenameRegex";

    public static IReadOnlyList<ConfigField> ConfigSchema => new[]
    {
        new ConfigField("pattern", ConfigFieldType.String, Label: "Regex Pattern", Required: true, Placeholder: @"\d+"),
        new ConfigField("replacement", ConfigFieldType.String, Label: "Replacement", Required: true),
        new ConfigField("scope", ConfigFieldType.Select, Label: "Scope", DefaultValue: "filename",
            Options: new[] { "filename", "fullpath" }),
    };

    private Regex _regex = null!;
    private string _replacement = string.Empty;
    private string _scope = "filename";

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("pattern", out JsonElement patternElement) ||
            patternElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("RenameRegex: 'pattern' is required.");
        }

        string pattern = patternElement.GetString()
            ?? throw new NodeConfigurationException("RenameRegex: 'pattern' must be a non-null string.");

        try
        {
            _regex = new Regex(pattern, RegexOptions.Compiled);
        }
        catch (ArgumentException ex)
        {
            throw new NodeConfigurationException($"RenameRegex: Invalid regex pattern '{pattern}': {ex.Message}", ex);
        }

        if (!config.TryGetValue("replacement", out JsonElement replacementElement) ||
            replacementElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("RenameRegex: 'replacement' is required.");
        }

        _replacement = replacementElement.GetString()
            ?? throw new NodeConfigurationException("RenameRegex: 'replacement' must be a non-null string.");

        if (config.TryGetValue("scope", out JsonElement scopeElement))
        {
            _scope = scopeElement.GetString() ?? "filename";
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string oldName = job.FileName;

        string oldPath = job.CurrentPath;
        string newPath;

        if (_scope.Equals("fullpath", StringComparison.OrdinalIgnoreCase))
        {
            newPath = _regex.Replace(job.CurrentPath, _replacement);
        }
        else
        {
            string directory = job.DirectoryName;
            string fileName = Path.GetFileName(job.CurrentPath);
            string newFileName = _regex.Replace(fileName, _replacement);
            newPath = Path.Combine(directory, newFileName);
        }

        if (!dryRun && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(oldPath, newPath, overwrite: false);
        }

        job.CurrentPath = newPath;
        job.NodeLog.Add($"RenameRegex: '{oldName}' → '{job.FileName}'");

        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }
}
