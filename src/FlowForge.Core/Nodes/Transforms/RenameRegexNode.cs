using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Transforms;

public class RenameRegexNode : ITransformNode
{
    private readonly ILogger<RenameRegexNode> _logger;

    public RenameRegexNode(ILogger<RenameRegexNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "RenameRegex";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("pattern", ConfigFieldType.String, Label: "Regex Pattern", Required: true, Placeholder: @"\d+", Description: @"Regular expression to match against filename (e.g. \d+ or \.jpe?g$)"),
        new ConfigField("replacement", ConfigFieldType.String, Label: "Replacement", Required: true, Description: "Replacement string ($1, $2 for capture groups)"),
        new ConfigField("scope", ConfigFieldType.Select, Label: "Scope", DefaultValue: "filename",
            Options: new[] { "filename", "fullpath" }, Description: "filename: match name only, fullpath: match entire path"),
    };

    private Regex _regex = null!;
    private string _replacement = string.Empty;
    private string _scope = "filename";

    public void Configure(IDictionary<string, JsonElement> config)
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
#pragma warning disable MA0023 // ExplicitCapture would break $1/$2 backreferences in replacement strings
            _regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2));
#pragma warning restore MA0023
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

        _logger.LogDebug("RenameRegex: configured with Pattern={Pattern}, Replacement={Replacement}, Scope={Scope}",
            _regex.ToString(), _replacement, _scope);
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            string oldName = job.FileName;

            string oldPath = job.CurrentPath;
            string newPath;

            if (_scope.Equals("fullpath", StringComparison.OrdinalIgnoreCase))
            {
                newPath = _regex.Replace(job.CurrentPath, _replacement);
                string originalDir = Path.GetDirectoryName(job.CurrentPath) ?? string.Empty;

                try
                {
                    PathGuard.EnsureWithinDirectory(newPath, originalDir);
                }
                catch (InvalidOperationException)
                {
                    string resolvedNew = Path.GetFullPath(newPath);
                    string resolvedDir = Path.GetFullPath(originalDir);
                    _logger.LogWarning("RenameRegex: path traversal blocked — {ResolvedPath} escapes {SourceDirectory}", resolvedNew, resolvedDir);
                    job.Status = FileJobStatus.Failed;
                    job.ErrorMessage = $"RenameRegex: path traversal blocked — '{resolvedNew}' escapes source directory '{resolvedDir}'.";
                    return Task.FromResult<IEnumerable<FileJob>>(new[] { job });
                }
            }
            else
            {
                string directory = job.DirectoryName;
                string fileName = Path.GetFileName(job.CurrentPath);
                string newFileName = _regex.Replace(fileName, _replacement);

                // Reject path separators injected via replacement (filename scope should produce a filename, not a path)
                if (newFileName.Contains(Path.DirectorySeparatorChar) ||
                    newFileName.Contains(Path.AltDirectorySeparatorChar) ||
                    newFileName.Contains("..", StringComparison.Ordinal))
                {
                    job.Status = FileJobStatus.Failed;
                    job.ErrorMessage = $"RenameRegex: replacement produced a path-like filename '{newFileName}'. Use fullpath scope for directory changes.";
                    return Task.FromResult<IEnumerable<FileJob>>(new[] { job });
                }

                newPath = Path.Combine(directory, newFileName);
                PathGuard.EnsureWithinDirectory(newPath, directory);
            }

            if (!dryRun && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                // .NET has no async File.Move/Copy API; sync call is acceptable for metadata-only operations
                File.Move(oldPath, newPath, overwrite: false);
            }

            job.CurrentPath = newPath;
            job.NodeLog.Add($"RenameRegex: '{oldName}' → '{job.FileName}'");

            IEnumerable<FileJob> result = new[] { job };
            return Task.FromResult(result);
        }
        catch (RegexMatchTimeoutException)
        {
            job.Status = FileJobStatus.Failed;
            job.ErrorMessage = "RenameRegex: regex match timed out — possible ReDoS pattern.";
            job.NodeLog.Add(job.ErrorMessage);
            return Task.FromResult<IEnumerable<FileJob>>(new[] { job });
        }
    }
}
