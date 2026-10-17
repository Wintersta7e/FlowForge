using System.Text.Json;
using System.Text.Json.Serialization;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Cli;

public class JsonOutputTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ITestOutputHelper _output;

    public JsonOutputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Json_jobs_after_a_run_carry_no_content_path()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(Path.Combine(input, "a.txt"), "a");
        string pipeline = Path.Combine(dir.Path, "copy.ffpipe");
        PipelineGraph graph = PipelineBuilder.New("CLI JSON jobs")
            .AddSource("FolderInput", new { path = input })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        await PipelineSerializer.SaveAsync(graph, pipeline, CancellationToken.None);

        CliRun run = await CliProcess.RunAsync(["run", pipeline, "--format", "json"], dir.Path, CancellationToken.None);

        string nl = Environment.NewLine;
        run.ExitCode.Should().Be(0, "{0}", $"stdout:{nl}{run.StdOut}{nl}stderr:{nl}{run.StdErr}");
        using var doc = JsonDocument.Parse(run.StdOut);
        JsonElement job = doc.RootElement.GetProperty("jobs")[0];
        string keys = string.Join(',', job.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        _output.WriteLine($"[cli] exit={run.ExitCode} job-keys={keys}");
        job.TryGetProperty("contentPath", out _).Should().BeFalse("job keys: {0}", keys);
    }

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
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        root.GetProperty("isDryRun").GetBoolean().Should().BeTrue();
        root.GetProperty("totalFiles").GetInt32().Should().Be(3);
        root.GetProperty("succeeded").GetInt32().Should().Be(2);
        root.GetProperty("failed").GetInt32().Should().Be(1);
        root.GetProperty("skipped").GetInt32().Should().Be(0);
        root.GetProperty("duration").ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("jobs").GetArrayLength().Should().Be(2);
        root.GetProperty("jobs")[1].GetProperty("status").GetString().Should().Be("failed");
        root.GetProperty("jobs")[1].GetProperty("errorMessage").GetString().Should().Be("File locked");
    }

    [Fact]
    public void ExecutionResult_roundtrips_all_fields()
    {
        var original = new ExecutionResult
        {
            IsDryRun = false,
            TotalFiles = 5,
            Succeeded = 3,
            Failed = 1,
            Skipped = 1,
            Duration = TimeSpan.FromSeconds(42),
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        ExecutionResult? deserialized = JsonSerializer.Deserialize<ExecutionResult>(json, JsonOptions);

        deserialized.Should().NotBeNull();
        deserialized!.TotalFiles.Should().Be(5);
        deserialized.Succeeded.Should().Be(3);
        deserialized.Failed.Should().Be(1);
        deserialized.Skipped.Should().Be(1);
        deserialized.IsDryRun.Should().BeFalse();
    }
}
