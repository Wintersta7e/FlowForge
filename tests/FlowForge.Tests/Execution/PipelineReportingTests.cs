using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class PipelineReportingTests
{
    private readonly ITestOutputHelper _output;

    public PipelineReportingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Dropped_and_failed_jobs_report_progress()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles(
            "keep1.txt", "keep2.txt", "keep3.txt", "keep4.txt", "keep5.txt",
            "drop1.dat", "drop2.dat", "drop3.dat", "drop4.dat", "drop5.dat");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*" })
            .AddTransform("Filter", new
            {
                conditions = new[]
                {
                    new { field = "extension", @operator = "equals", value = ".txt" }
                }
            })
            .AddTransform("RenameRegex", new { pattern = @"keep1\.txt", replacement = "../escaped.txt", scope = "fullpath" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        var progress = new SyncProgress<PipelineProgressEvent>();

        ExecutionResult result = await CreateRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), dir.StagingRoot)
            .RunAsync(graph, dryRun: true, progress);

        var events = progress.Events.OfType<FileProcessed>().ToList();
        int completing = events.Count(e => e.CompletesOriginal);
        _output.WriteLine(
            $"[report] events={events.Count} succeeded={result.Succeeded} failed={result.Failed} " +
            $"skipped={result.Skipped} total={result.TotalFiles} completing={completing}");
        events.Should().HaveCount(10);
        (result.Succeeded + result.Failed + result.Skipped).Should().Be(10);
        result.Succeeded.Should().Be(4);
        result.Failed.Should().Be(1);
        result.Skipped.Should().Be(5);
        events.Should().OnlyContain(e => e.CompletesOriginal);
        completing.Should().Be(result.TotalFiles).And.Be(10);
        int running = 0;
        foreach (FileProcessed processed in events)
        {
            running += processed.CompletesOriginal ? 1 : 0;
            running.Should().BeLessThanOrEqualTo(result.TotalFiles);
        }
    }

    [Fact]
    public async Task Extra_jobs_from_one_input_complete_their_original_once()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register<DuplicatingTransform>(
            DuplicatingTransform.RegisteredTypeKey,
            () => new DuplicatingTransform(),
            "Duplicate Job",
            NodeCategory.Transform,
            () => Array.Empty<ConfigField>());
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddTransform(DuplicatingTransform.RegisteredTypeKey, new { })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        var progress = new SyncProgress<PipelineProgressEvent>();

        ExecutionResult result = await CreateRunner(registry, dir.StagingRoot).RunAsync(graph, dryRun: true, progress);

        var events = progress.Events.OfType<FileProcessed>().ToList();
        var completing = events.Where(e => e.CompletesOriginal).ToList();
        _output.WriteLine($"[lineage] events={events.Count} completing={completing.Count} total={result.TotalFiles}");
        events.Should().HaveCount(4);
        completing.Should().HaveCount(2);
        result.TotalFiles.Should().Be(2);
        completing.Select(e => e.Job.OriginalPath).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(events.Select(e => e.Job.OriginalPath).Distinct(StringComparer.Ordinal));
    }

    private static PipelineRunner CreateRunner(NodeRegistry registry, string stagingRoot) =>
        new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 2, stagingRoot: stagingRoot);

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _events = new();
        private readonly Lock _lock = new();

        public IReadOnlyList<T> Events
        {
            get { lock (_lock) { return _events.ToList(); } }
        }

        public void Report(T value)
        {
            lock (_lock)
            { _events.Add(value); }
        }
    }

    /// <summary>Returns each job together with one extra job for a copy beside it.</summary>
    private sealed class DuplicatingTransform : ITransformNode
    {
        public const string RegisteredTypeKey = "DuplicateJob";

        public string TypeKey => RegisteredTypeKey;

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            var extra = new FileJob
            {
                OriginalPath = job.OriginalPath,
                CurrentPath = Path.Combine(job.DirectoryName, "copy-" + job.FileName),
            };
            return Task.FromResult<IEnumerable<FileJob>>(new[] { job, extra });
        }
    }
}
