using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Execution;

public class PipelineRunnerProgressTests
{
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly List<T> _events = new();
        private readonly object _lock = new();

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

    private static PipelineRunner CreateRunner()
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        return new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 2);
    }

    [Fact]
    public async Task RunAsync_ReportsPhaseChanged_Enumerating_First()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        await runner.RunAsync(pipeline, dryRun: true, progress);

        progress.Events.Should().NotBeEmpty();
        progress.Events[0].Should().Be(new PhaseChanged(ExecutionPhase.Enumerating));
    }

    [Fact]
    public async Task RunAsync_ReportsFilesDiscovered_DuringEnumeration()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        await runner.RunAsync(pipeline, dryRun: true, progress);

        var discovered = progress.Events.OfType<FilesDiscovered>().ToList();
        discovered.Should().NotBeEmpty();
        discovered[discovered.Count - 1].TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_ReportsPhaseChanged_Processing_AfterEnumeration()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        await runner.RunAsync(pipeline, dryRun: true, progress);

        int enumeratingIndex = progress.Events.ToList()
            .FindIndex(e => e is PhaseChanged pc && pc.Phase == ExecutionPhase.Enumerating);
        int processingIndex = progress.Events.ToList()
            .FindIndex(e => e is PhaseChanged pc && pc.Phase == ExecutionPhase.Processing);

        enumeratingIndex.Should().BeGreaterThanOrEqualTo(0);
        processingIndex.Should().BeGreaterThan(enumeratingIndex);
    }

    [Fact]
    public async Task RunAsync_ReportsFileProcessed_PerJob()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        await runner.RunAsync(pipeline, dryRun: true, progress);

        var processed = progress.Events.OfType<FileProcessed>().ToList();
        processed.Should().HaveCount(3);
        processed.Should().OnlyContain(fp => fp.Job.Status == FileJobStatus.Succeeded);
    }

    [Fact]
    public async Task RunAsync_ReportsPhaseChanged_Complete_Last()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        await runner.RunAsync(pipeline, dryRun: true, progress);

        progress.Events.Should().NotBeEmpty();
        progress.Events[progress.Events.Count - 1].Should().Be(new PhaseChanged(ExecutionPhase.Complete));
    }

    [Fact]
    public async Task RunAsync_Cancelled_DoesNotReportComplete()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Cancel Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        var progress = new SyncProgress<PipelineProgressEvent>();
        PipelineRunner runner = CreateRunner();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => runner.RunAsync(pipeline, dryRun: true, progress, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        progress.Events.OfType<PhaseChanged>()
            .Should().NotContain(p => p.Phase == ExecutionPhase.Complete);
    }

    [Fact]
    public async Task RunAsync_WithNullProgress_CompletesSuccessfully()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Null Progress Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();

        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: true, progress: null);

        result.TotalFiles.Should().Be(1);
        result.Succeeded.Should().Be(1);
    }
}
