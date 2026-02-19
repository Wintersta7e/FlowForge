using System.Text.Json;
using System.Text.RegularExpressions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Nodes.Transforms;

public class FilterNode : ITransformNode
{
    public string TypeKey => "Filter";

    private List<FilterCondition> _conditions = new();

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("conditions", out JsonElement conditionsElement) ||
            conditionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new NodeConfigurationException("Filter: 'conditions' array is required.");
        }

        _conditions = new List<FilterCondition>();
        foreach (JsonElement condElement in conditionsElement.EnumerateArray())
        {
            string field = condElement.GetProperty("field").GetString()
                ?? throw new NodeConfigurationException("Filter: condition 'field' is required.");
            string op = condElement.GetProperty("operator").GetString()
                ?? throw new NodeConfigurationException("Filter: condition 'operator' is required.");
            string value = condElement.GetProperty("value").GetString()
                ?? throw new NodeConfigurationException("Filter: condition 'value' is required.");

            _conditions.Add(new FilterCondition(field, op, value));
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        bool matches = _conditions.All(c => EvaluateCondition(c, job));

        if (matches)
        {
            job.NodeLog.Add("Filter: passed");
            IEnumerable<FileJob> result = new[] { job };
            return Task.FromResult(result);
        }

        job.NodeLog.Add("Filter: dropped");
        job.Status = FileJobStatus.Skipped;
        return Task.FromResult(Enumerable.Empty<FileJob>());
    }

    private static bool EvaluateCondition(FilterCondition condition, FileJob job)
    {
        string fieldValue = GetFieldValue(condition.Field, job);

        return condition.Operator.ToLowerInvariant() switch
        {
            "equals" => fieldValue.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "notequals" => !fieldValue.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "contains" => fieldValue.Contains(condition.Value, StringComparison.OrdinalIgnoreCase),
            "startswith" => fieldValue.StartsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
            "endswith" => fieldValue.EndsWith(condition.Value, StringComparison.OrdinalIgnoreCase),
            "greaterthan" => CompareNumeric(fieldValue, condition.Value) > 0,
            "lessthan" => CompareNumeric(fieldValue, condition.Value) < 0,
            "matches" => Regex.IsMatch(fieldValue, condition.Value, RegexOptions.IgnoreCase),
            _ => throw new InvalidOperationException($"Unknown filter operator: '{condition.Operator}'")
        };
    }

    private static string GetFieldValue(string field, FileJob job)
    {
        return field.ToLowerInvariant() switch
        {
            "extension" => job.Extension,
            "filename" => job.FileName,
            "size" => GetFileSize(job.CurrentPath),
            "createdat" => GetFileCreatedAt(job.CurrentPath),
            "modifiedat" => GetFileModifiedAt(job.CurrentPath),
            _ => throw new InvalidOperationException($"Unknown filter field: '{field}'")
        };
    }

    private static string GetFileSize(string path)
    {
        if (!File.Exists(path)) return "0";
        var info = new FileInfo(path);
        return info.Length.ToString();
    }

    private static string GetFileCreatedAt(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        return File.GetCreationTimeUtc(path).ToString("o");
    }

    private static string GetFileModifiedAt(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        return File.GetLastWriteTimeUtc(path).ToString("o");
    }

    private static int CompareNumeric(string a, string b)
    {
        if (long.TryParse(a, out long numA) && long.TryParse(b, out long numB))
        {
            return numA.CompareTo(numB);
        }
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FilterCondition(string Field, string Operator, string Value);
}
