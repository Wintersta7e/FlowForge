using System.Text.Json;
using System.Text.Json.Serialization;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FluentAssertions;

namespace FlowForge.Tests.Cli;

public class JsonOutputTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Fact]
    public void ExecutionResult_serializes_to_expected_json_shape()
    {
        var result = new ExecutionResult
        {
            IsDryRun = true,
            TotalFiles = 3,
            Succeeded = 2,
            Failed = 1,
            Skipped = 0,
            Duration = TimeSpan.FromMilliseconds(1234),
        };
        result.Jobs.Add(new FileJob
        {
            OriginalPath = "/input/a.jpg",
            CurrentPath = "/output/a.jpg",
            Status = FileJobStatus.Succeeded,
        });
        result.Jobs.Add(new FileJob
        {
            OriginalPath = "/input/b.jpg",
            CurrentPath = "/input/b.jpg",
            Status = FileJobStatus.Failed,
            ErrorMessage = "File locked",
        });

        string json = JsonSerializer.Serialize(result, JsonOptions);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.GetProperty("isDryRun").GetBoolean().Should().BeTrue();
        root.GetProperty("totalFiles").GetInt32().Should().Be(3);
        root.GetProperty("succeeded").GetInt32().Should().Be(2);
        root.GetProperty("failed").GetInt32().Should().Be(1);
        root.GetProperty("jobs").GetArrayLength().Should().Be(2);
        root.GetProperty("jobs")[1].GetProperty("status").GetString().Should().Be("failed");
        root.GetProperty("jobs")[1].GetProperty("errorMessage").GetString().Should().Be("File locked");
    }
}
