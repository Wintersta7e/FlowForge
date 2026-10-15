using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Transforms;

public class RenamePatternNode : ITransformNode
{
    private readonly ILogger<RenamePatternNode> _logger;

    public RenamePatternNode(ILogger<RenamePatternNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "RenamePattern";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("pattern", ConfigFieldType.String, Label: "Rename Pattern", Required: true, Placeholder: "{name}_{counter:000}{ext}", Description: "Tokens: {name}, {ext}, {counter:000}, {date:yyyy-MM-dd}, {meta:KEY}"),
        new ConfigField("startIndex", ConfigFieldType.Int, Label: "Counter Start", DefaultValue: "1", Description: "Starting number for {counter} token"),
    };

    private string _pattern = string.Empty;
    private int _startIndex = 1;
    private int _counter;

    private static readonly Regex TokenRegex = new(
        @"\{(?<token>name|ext|counter|date|meta)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    public void Configure(IDictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("pattern", out JsonElement patternElement) ||
            patternElement.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("RenamePattern: 'pattern' is required.");
        }

        _pattern = patternElement.GetString()
            ?? throw new NodeConfigurationException("RenamePattern: 'pattern' must be a non-null string.");

        if (string.IsNullOrWhiteSpace(_pattern))
        {
            throw new NodeConfigurationException("RenamePattern: 'pattern' is empty — set a rename pattern like '{name}_{counter:000}{ext}' on the Rename Pattern station.");
        }

        if (config.TryGetValue("startIndex", out JsonElement startIndexElement))
        {
            _startIndex = startIndexElement.GetInt32();
        }

        _counter = _startIndex - 1;

        _logger.LogDebug("RenamePattern: configured with Pattern={Pattern}, StartIndex={StartIndex}", _pattern, _startIndex);

        // Validate date format tokens eagerly so bad patterns fail at configure time
        MatchCollection dateMatches = TokenRegex.Matches(_pattern);
        foreach (Match m in dateMatches)
        {
            if (string.Equals(m.Groups["token"].Value, "date", StringComparison.Ordinal) && !string.IsNullOrEmpty(m.Groups["format"].Value))
            {
                try
                {
                    DateTime.Today.ToString(m.Groups["format"].Value, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new NodeConfigurationException(
                        $"RenamePattern: invalid date format '{m.Groups["format"].Value}': {ex.Message}", ex);
                }
            }
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string oldName = job.FileName;
        string directory = job.DirectoryName;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(job.CurrentPath);
        string extension = Path.GetExtension(job.CurrentPath);

        int currentCounter = Interlocked.Increment(ref _counter);

        string newName = TokenRegex.Replace(_pattern, match =>
        {
            string token = match.Groups["token"].Value;
            string format = match.Groups["format"].Value;

            return token switch
            {
                "name" => nameWithoutExt,
                "ext" => extension,
                "counter" => string.IsNullOrEmpty(format)
                    ? currentCounter.ToString(CultureInfo.InvariantCulture)
                    : currentCounter.ToString(format, CultureInfo.InvariantCulture),
                "date" => ResolveDateToken(format),
                "meta" => ResolveMetaToken(job, format, context),
                _ => match.Value
            };
        });

        string newPath = Path.Combine(directory, newName);

        PathGuard.EnsureWithinDirectory(newPath, directory);

        // The rename is logical: only the name changes; the output resolves name clashes.
        job.CurrentPath = newPath;
        job.NodeLog.Add($"RenamePattern: '{oldName}' → '{Path.GetFileName(newPath)}'");

        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }

    /// <summary>
    /// A missing key resolves to an empty string. A dry run reads no file content, so a key missing there may
    /// exist in the real run: the job's preview is unresolved.
    /// </summary>
    private static string ResolveMetaToken(FileJob job, string format, RunContext context)
    {
        if (!string.IsNullOrEmpty(format) && job.Metadata.TryGetValue(format, out string? metaValue))
        {
            return metaValue;
        }

        if (context.DryRun)
        {
            job.PreviewMarker = PreviewMarker.Unresolved;
        }

        return string.Empty;
    }

    private static string ResolveDateToken(string? format)
    {
        try
        {
            return string.IsNullOrEmpty(format)
                ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : DateTime.Today.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
