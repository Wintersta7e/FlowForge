using System.Collections.Concurrent;
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

public class RunnerEntryTests
{
    private readonly ITestOutputHelper _output;

    public RunnerEntryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Transforms_run_off_the_callers_context()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");
        var recorder = new ContextRecordingTransform();
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register<ContextRecordingTransform>(
            ContextRecordingTransform.RegisteredTypeKey,
            () => recorder,
            "Context Recorder",
            NodeCategory.Transform,
            () => Array.Empty<ConfigField>());
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddTransform(ContextRecordingTransform.RegisteredTypeKey, new { })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        var runner = new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot);
        using var caller = new SingleThreadContext();

        SynchronizationContext? saved = SynchronizationContext.Current;
        Task<ExecutionResult> run;
        SynchronizationContext.SetSynchronizationContext(caller);
        try
        {
            run = runner.RunAsync(graph, dryRun: true);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(saved);
        }

        caller.RunUntil(run);
        ExecutionResult result = await run;

        SynchronizationContext? observed = recorder.Observed;
        bool same = ReferenceEquals(observed, caller);
        _output.WriteLine(
            $"[context] caller={caller.GetType().Name} observed={observed?.GetType().Name ?? "null"} same={same}");
        recorder.Calls.Should().Be(1);
        result.Succeeded.Should().Be(1);
        observed.Should().NotBeSameAs(caller);
    }

    [Fact]
    public async Task A_refused_pipeline_reports_no_progress()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");
        var runner = new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot);

        // The editor lets one step feed two outputs; the run refuses that shape after the graph checks pass it.
        NodeDefinition input = PipelineBuilder.Node("FolderInput", new { path = dir.Path, recursive = false });
        NodeDefinition rename = PipelineBuilder.Node("RenameAddAffix", new { prefix = "x_" });
        NodeDefinition first = PipelineBuilder.Node("FolderOutput", new { path = Path.Combine(dir.OutputPath, "first"), mode = "copy" });
        NodeDefinition second = PipelineBuilder.Node("FolderOutput", new { path = Path.Combine(dir.OutputPath, "second"), mode = "copy" });
        var fanOut = new PipelineGraph { Name = "fan-out", Nodes = [input, rename, first, second] };
        fanOut.Connections.Add(new Connection { FromNode = input.Id, ToNode = rename.Id });
        fanOut.Connections.Add(new Connection { FromNode = rename.Id, ToNode = first.Id });
        fanOut.Connections.Add(new Connection { FromNode = rename.Id, ToNode = second.Id });

        foreach (PipelineGraph graph in new[] { new PipelineGraph { Name = "empty" }, fanOut })
        {
            var progress = new RecordingProgress();
            Func<Task> run = () => runner.RunAsync(graph, dryRun: true, progress);

            InvalidOperationException refused = (await run.Should().ThrowAsync<InvalidOperationException>()).Which;
            _output.WriteLine($"[refused] graph={graph.Name} events={string.Join(",", progress.Events)} message='{refused.Message}'");
            progress.Events.Should().BeEmpty("a refused pipeline never starts a scan, so nothing reports one");
        }
    }

    /// <summary>Keeps every reported event, in order, on the reporting thread.</summary>
    private sealed class RecordingProgress : IProgress<PipelineProgressEvent>
    {
        public ConcurrentQueue<PipelineProgressEvent> Events { get; } = new();

        public void Report(PipelineProgressEvent value) => Events.Enqueue(value);
    }

    /// <summary>Queues posted callbacks and runs them on the thread that calls <see cref="RunUntil"/>.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void RunUntil(Task task)
        {
            _ = task.ContinueWith(
                _ => _queue.CompleteAdding(),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
            foreach ((SendOrPostCallback callback, object? state) in _queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        }

        public void Dispose() => _queue.Dispose();
    }

    /// <summary>Records the synchronization context it runs under and passes the job on.</summary>
    private sealed class ContextRecordingTransform : ITransformNode
    {
        public const string RegisteredTypeKey = "ContextRecorder";

        public string TypeKey => RegisteredTypeKey;

        public SynchronizationContext? Observed { get; private set; }

        public int Calls { get; private set; }

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            Observed = SynchronizationContext.Current;
            Calls++;
            return Task.FromResult<IEnumerable<FileJob>>(new[] { job });
        }
    }
}
