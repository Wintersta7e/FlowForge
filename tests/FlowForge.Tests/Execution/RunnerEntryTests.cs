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
        var runner = new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance);
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

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
        {
            Observed = SynchronizationContext.Current;
            Calls++;
            return Task.FromResult<IEnumerable<FileJob>>(new[] { job });
        }
    }
}
