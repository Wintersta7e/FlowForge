using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Transforms;

public class RenamePatternNode : ITransformNode
{
    public string TypeKey => "RenamePattern";

    private string _pattern = string.Empty;
    private int _startIndex = 1;
    private int _counter;

    private static readonly Regex TokenRegex = new(
        @"\{(?<token>name|ext|counter|date|meta)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled);

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("pattern", out JsonElement patternElement) ||
            patternElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("RenamePattern: 'pattern' is required.");
        }

        _pattern = patternElement.GetString()
            ?? throw new NodeConfigurationException("RenamePattern: 'pattern' must be a non-null string.");

        if (config.TryGetValue("startIndex", out JsonElement startIndexElement))
        {
            _startIndex = startIndexElement.GetInt32();
        }

        _counter = _startIndex;
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string oldName = job.FileName;
        string directory = job.DirectoryName;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(job.CurrentPath);
        string extension = Path.GetExtension(job.CurrentPath);

        int currentCounter = Interlocked.Increment(ref _counter) - 1;

        string newName = TokenRegex.Replace(_pattern, match =>
        {
            string token = match.Groups["token"].Value;
            string format = match.Groups["format"].Value;

            return token switch
            {
                "name" => nameWithoutExt,
                "ext" => extension,
                "counter" => string.IsNullOrEmpty(format)
                    ? currentCounter.ToString()
                    : currentCounter.ToString(format),
                "date" => string.IsNullOrEmpty(format)
                    ? DateTime.Today.ToString("yyyy-MM-dd")
                    : DateTime.Today.ToString(format),
                "meta" => !string.IsNullOrEmpty(format) && job.Metadata.TryGetValue(format, out string? metaValue)
                    ? metaValue
                    : string.Empty,
                _ => match.Value
            };
        });

        string oldPath = job.CurrentPath;
        string newPath = Path.Combine(directory, newName);

        if (!dryRun)
        {
            newPath = ResolveConflict(newPath);
            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(oldPath, newPath, overwrite: false);
            }
        }

        job.CurrentPath = newPath;
        job.NodeLog.Add($"RenamePattern: '{oldName}' → '{Path.GetFileName(newPath)}'");

        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }

    private static string ResolveConflict(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        int suffix = 1;
        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{nameWithoutExt}_{suffix}{extension}");
            suffix++;
        } while (File.Exists(candidate));

        return candidate;
    }
}
