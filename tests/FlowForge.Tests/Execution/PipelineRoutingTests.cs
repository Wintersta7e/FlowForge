using System.Security.Cryptography;
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

public class PipelineRoutingTests
{
    private readonly ITestOutputHelper _output;

    public PipelineRoutingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Unconnected_transform_is_refused_naming_it()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        NodeDefinition stray = PipelineBuilder.Node("RenameAddAffix", new { prefix = "x_" });
        graph.Nodes.Add(stray);

        Func<Task> act = () => CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        string expected = $"Node 'RenameAddAffix' ({stray.Id}) is not on a path from a source to an output.";
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*" + expected + "*");
        SourceNames(dir).Should().BeEquivalentTo("a.txt", "b.txt");
        dir.OutputFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Connection_into_a_source_is_refused()
    {
        using var dirA = new TempDirectory();
        using var dirB = new TempDirectory();
        dirA.CreateFiles("a.txt", "b.txt");
        dirB.CreateFiles("a.txt", "b.txt");
        NodeDefinition inA = PipelineBuilder.Node("FolderInput", new { path = dirA.Path, recursive = false });
        NodeDefinition inB = PipelineBuilder.Node("FolderInput", new { path = dirB.Path, recursive = false });
        NodeDefinition output = PipelineBuilder.Node("FolderOutput", new { path = dirA.OutputPath, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { inA, inB, output },
            Connections = { Wire(inB, inA), Wire(inA, output) },
        };

        Func<Task> act = () => CreateRunner(dirA.StagingRoot).RunAsync(graph, dryRun: false);

        string expected = $"Connection from 'FolderInput' ({inB.Id}) into source node 'FolderInput' ({inA.Id}) is not allowed.";
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*" + expected + "*");
        SourceNames(dirA).Should().BeEquivalentTo("a.txt", "b.txt");
        SourceNames(dirB).Should().BeEquivalentTo("a.txt", "b.txt");
        dirA.OutputFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Connection_out_of_an_output_is_refused()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        string secondOutput = Path.Combine(dir.Path, "second");
        NodeDefinition input = PipelineBuilder.Node("FolderInput", new { path = dir.Path, recursive = false });
        NodeDefinition outA = PipelineBuilder.Node("FolderOutput", new { path = dir.OutputPath, mode = "copy" });
        NodeDefinition outB = PipelineBuilder.Node("FolderOutput", new { path = secondOutput, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { input, outA, outB },
            Connections = { Wire(input, outA), Wire(outA, outB) },
        };

        Func<Task> act = () => CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        string expected = $"Connection out of output node 'FolderOutput' ({outA.Id}) to 'FolderOutput' ({outB.Id}) is not allowed.";
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*" + expected + "*");
        SourceNames(dir).Should().BeEquivalentTo("a.txt", "b.txt");
        dir.OutputFiles.Should().BeEmpty();
        Directory.Exists(secondOutput).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_connection_runs_once()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();
        graph.Connections.Add(new Connection { FromNode = graph.Nodes[0].Id, ToNode = graph.Nodes[1].Id });

        ExecutionResult result = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        _output.WriteLine($"[routing] delivered={dir.OutputFiles.Length}");
        dir.OutputFiles.Should().NotBeEmpty();
        result.Succeeded.Should().Be(2);
        result.Failed.Should().Be(0);
        result.Jobs.Should().HaveCount(2);
        dir.OutputFiles.Should().BeEquivalentTo("a.txt", "b.txt");
    }

    [Fact]
    public async Task Sorted_counter_names_are_identical_at_any_concurrency()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["001.txt"] = "test content: alpha.txt",
            ["002.txt"] = "test content: bravo.txt",
            ["003.txt"] = "test content: charlie.txt",
            ["004.txt"] = "test content: delta.txt",
            ["005.txt"] = "test content: echo.txt",
        };
        int runs = 0;
        int names = 0;

        foreach (int concurrency in new[] { 1, Environment.ProcessorCount })
        {
            for (int run = 0; run < 5; run++)
            {
                using var dir = new TempDirectory();
                dir.CreateFiles("charlie.txt", "alpha.txt", "echo.txt", "bravo.txt", "delta.txt");
                PipelineGraph graph = PipelineBuilder.New()
                    .AddSource("FolderInput", new { path = dir.Path, recursive = false })
                    .AddTransform("Sort", new { field = "filename", direction = "asc" })
                    .AddTransform("RenamePattern", new { pattern = "{counter:000}{ext}" })
                    .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
                    .Build();

                await CreateRunner(dir.StagingRoot, concurrency).RunAsync(graph, dryRun: false);

                Dictionary<string, string> delivered = dir.OutputFiles.ToDictionary(
                    name => name,
                    name => File.ReadAllText(Path.Combine(dir.OutputPath, name)),
                    StringComparer.Ordinal);
                delivered.Should().Equal(expected, $"run {run} at concurrency {concurrency}");
                runs++;
                names = delivered.Count;
            }
        }

        _output.WriteLine($"[counter] runs={runs} names={names}");
        runs.Should().BePositive();
        names.Should().BePositive();
    }

    [Fact]
    public async Task Output_concurrency_never_exceeds_the_cap()
    {
        const int cap = 2;
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt", "c.txt", "d.txt", "e.txt", "f.txt");
        var counter = new PeakCountingOutput(cap);
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register<PeakCountingOutput>(
            PeakCountingOutput.RegisteredTypeKey,
            () => counter,
            "Peak Counter",
            NodeCategory.Output,
            () => Array.Empty<ConfigField>());
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false })
            .AddOutput(PeakCountingOutput.RegisteredTypeKey, new { })
            .Build();
        var runner = new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: cap, stagingRoot: dir.StagingRoot);

        // A runner that never lets two calls overlap would leave the first call waiting; fail instead of hanging.
        using var hangGuard = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        ExecutionResult result = await runner.RunAsync(graph, dryRun: false, ct: hangGuard.Token);

        _output.WriteLine($"[concurrency] cap={cap} peak={counter.Peak} calls={counter.Calls}");
        counter.Calls.Should().Be(6);
        result.Succeeded.Should().Be(6);
        counter.Peak.Should().Be(cap);
    }

    [Fact]
    public async Task Filtered_and_unfiltered_branches_receive_their_own_jobs()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in", "a.txt", "b.txt", "c.dat", "d.csv");
        string outA = Path.Combine(dir.Path, "outA");
        string outB = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input, recursive = false });
        NodeDefinition filter = PipelineBuilder.Node("Filter", new
        {
            conditions = new[] { new { field = "extension", @operator = "equals", value = ".txt" } },
        });
        NodeDefinition filtered = PipelineBuilder.Node("FolderOutput", new { path = outA, mode = "copy" });
        NodeDefinition unfiltered = PipelineBuilder.Node("FolderOutput", new { path = outB, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, filter, filtered, unfiltered },
            Connections = { Wire(source, filter), Wire(filter, filtered), Wire(source, unfiltered) },
        };

        ExecutionResult result = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        Dictionary<string, string> sources = Hashes(input);
        Dictionary<string, string> deliveredA = Hashes(outA);
        Dictionary<string, string> deliveredB = Hashes(outB);
        _output.WriteLine($"[branches] outA: {Listing(deliveredA)}");
        _output.WriteLine($"[branches] outB: {Listing(deliveredB)}");
        deliveredA.Should().Equal(ToDictionary(sources.Where(p => p.Key.EndsWith(".txt", StringComparison.Ordinal))));
        deliveredB.Should().Equal(sources);
        result.Succeeded.Should().Be(6);
        result.Failed.Should().Be(0);
    }

    [Fact]
    public async Task Two_sources_deliver_their_union()
    {
        using var dir = new TempDirectory();
        string first = CreateFolder(dir, "first", "a.txt", "b.txt");
        string second = CreateFolder(dir, "second", "c.txt", "d.txt");
        NodeDefinition inA = PipelineBuilder.Node("FolderInput", new { path = first, recursive = false });
        NodeDefinition inB = PipelineBuilder.Node("FolderInput", new { path = second, recursive = false });
        NodeDefinition output = PipelineBuilder.Node("FolderOutput", new { path = dir.OutputPath, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { inA, inB, output },
            Connections = { Wire(inA, output), Wire(inB, output) },
        };

        ExecutionResult result = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        Dictionary<string, string> delivered = Hashes(dir.OutputPath);
        _output.WriteLine($"[union] total={result.TotalFiles} out: {Listing(delivered)}");
        delivered.Should().Equal(ToDictionary(Hashes(first).Concat(Hashes(second))));
        result.TotalFiles.Should().Be(4);
        result.Succeeded.Should().Be(4);
    }

    [Fact]
    public async Task Diamond_delivers_one_file_per_path()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in", "a.txt", "b.txt", "c.txt");
        var passAll = new { conditions = new[] { new { field = "extension", @operator = "notequals", value = ".none" } } };
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input, recursive = false });
        NodeDefinition left = PipelineBuilder.Node("Filter", passAll);
        NodeDefinition right = PipelineBuilder.Node("Filter", passAll);
        NodeDefinition merge = PipelineBuilder.Node("Sort", new { field = "filename", direction = "asc" });
        NodeDefinition output = PipelineBuilder.Node("FolderOutput", new { path = dir.OutputPath, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, left, right, merge, output },
            Connections = { Wire(source, left), Wire(source, right), Wire(left, merge), Wire(right, merge), Wire(merge, output) },
        };

        ExecutionResult result = await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: false);

        Dictionary<string, string> sources = Hashes(input);
        Dictionary<string, string> delivered = Hashes(dir.OutputPath);
        _output.WriteLine($"[diamond] succeeded={result.Succeeded} out: {Listing(delivered)}");
        delivered.Should().Equal(ToDictionary(sources.Concat(sources.Select(p =>
            KeyValuePair.Create(Path.GetFileNameWithoutExtension(p.Key) + "_1" + Path.GetExtension(p.Key), p.Value)))));
        result.Succeeded.Should().Be(2 * result.TotalFiles).And.Be(6);
    }

    [Fact]
    public async Task Branches_after_a_convert_each_deliver_the_converted_file()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        TestFileFactory.CreateTestImage(Path.Combine(input, "photo.jpg"));
        string outA = Path.Combine(dir.Path, "outA");
        string outB = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input, recursive = false });
        NodeDefinition convert = PipelineBuilder.Node("ImageConvert", new { format = "png" });
        NodeDefinition first = PipelineBuilder.Node("FolderOutput", new { path = outA, mode = "copy" });
        NodeDefinition second = PipelineBuilder.Node("FolderOutput", new { path = outB, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, convert, first, second },
            Connections = { Wire(source, convert), Wire(convert, first), Wire(convert, second) },
        };

        // One delivery at a time: the first branch ends before the second reads the converted file they share.
        ExecutionResult result = await CreateRunner(dir.StagingRoot, maxConcurrency: 1).RunAsync(graph, dryRun: false);

        Dictionary<string, string> deliveredA = Hashes(outA);
        Dictionary<string, string> deliveredB = Hashes(outB);
        string failures = string.Join("; ", result.Jobs.Where(j => j.Status == FileJobStatus.Failed).Select(j => j.ErrorMessage));
        _output.WriteLine($"[convert-branches] outA: {Listing(deliveredA)} outB: {Listing(deliveredB)} failures: {failures}");
        result.Succeeded.Should().Be(2, failures);
        deliveredA.Keys.Should().Equal("photo.png");
        deliveredB.Should().Equal(deliveredA);
        (await File.ReadAllBytesAsync(Path.Combine(outB, "photo.png"))).Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
        Directory.GetFiles(input).Select(f => Path.GetFileName(f)).Should().Equal("photo.jpg");
    }

    [Fact]
    public async Task Buffered_node_that_returns_a_job_it_was_not_given_is_refused()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        (PipelineGraph graph, NodeDefinition buffer, PipelineRunner runner) = MisbehavingBufferRun(dir, addsAJob: true);

        Func<Task> act = () => runner.RunAsync(graph, dryRun: false);

        string message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        _output.WriteLine($"[flush] {message}");
        message.Should().Contain($"'{MisbehavingBufferNode.RegisteredTypeKey}' ({buffer.Id})");
        dir.OutputFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Buffered_node_that_drops_a_job_is_refused()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        (PipelineGraph graph, NodeDefinition buffer, PipelineRunner runner) = MisbehavingBufferRun(dir, addsAJob: false);

        Func<Task> act = () => runner.RunAsync(graph, dryRun: false);

        string message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        _output.WriteLine($"[flush] {message}");
        message.Should().Contain($"'{MisbehavingBufferNode.RegisteredTypeKey}' ({buffer.Id})");
        dir.OutputFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Extra_with_its_own_content_after_a_convert_delivers_both_files()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        TestFileFactory.CreateTestImage(Path.Combine(input, "photo.jpg"));
        string output = Path.Combine(dir.Path, "out");
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register(VariantTransform.RegisteredTypeKey, () => new VariantTransform(), "Variant", NodeCategory.Transform, () => []);
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, recursive = false })
            .AddTransform("ImageConvert", new { format = "png" })
            .AddTransform(VariantTransform.RegisteredTypeKey, new { })
            .AddOutput("FolderOutput", new { path = output, mode = "copy" })
            .Build();

        ExecutionResult result = await new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 1, dir.StagingRoot).RunAsync(graph);

        Dictionary<string, string> delivered = Hashes(output);
        string failures = string.Join("; ", result.Jobs.Where(j => j.Status == FileJobStatus.Failed).Select(j => j.ErrorMessage));
        _output.WriteLine($"[restaged-extra] out: {Listing(delivered)} failures: {failures}");
        result.Succeeded.Should().Be(2, failures);
        delivered.Keys.Should().BeEquivalentTo(["photo.png", "variant-photo.png"], failures);
        delivered["variant-photo.png"].Should().Be(delivered["photo.png"]);
    }

    [Fact]
    public async Task Extra_not_made_by_the_run_is_refused_naming_the_node()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register(HandBuiltExtraTransform.RegisteredTypeKey, () => new HandBuiltExtraTransform(), "Hand-built Extra", NodeCategory.Transform, () => []);
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = dir.Path, recursive = false });
        NodeDefinition extra = PipelineBuilder.Node(HandBuiltExtraTransform.RegisteredTypeKey, new { });
        NodeDefinition output = PipelineBuilder.Node("FolderOutput", new { path = dir.OutputPath, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, extra, output },
            Connections = { Wire(source, extra), Wire(extra, output) },
        };
        var runner = new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 1, stagingRoot: dir.StagingRoot);

        Func<Task> act = () => runner.RunAsync(graph, dryRun: false);

        string message = (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message;
        _output.WriteLine($"[hand-built-extra] {message}");
        message.Should().Contain($"'{HandBuiltExtraTransform.RegisteredTypeKey}' ({extra.Id})");
        dir.OutputFiles.Should().BeEmpty();
    }

    private static (PipelineGraph Graph, NodeDefinition Buffer, PipelineRunner Runner) MisbehavingBufferRun(TempDirectory dir, bool addsAJob)
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register(MisbehavingBufferNode.RegisteredTypeKey, () => new MisbehavingBufferNode(addsAJob), "Misbehaving Buffer", NodeCategory.Transform, () => []);
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = dir.Path, recursive = false });
        NodeDefinition buffer = PipelineBuilder.Node(MisbehavingBufferNode.RegisteredTypeKey, new { });
        NodeDefinition output = PipelineBuilder.Node("FolderOutput", new { path = dir.OutputPath, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, buffer, output },
            Connections = { Wire(source, buffer), Wire(buffer, output) },
        };
        return (graph, buffer, new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 2, stagingRoot: dir.StagingRoot));
    }

    private static PipelineRunner CreateRunner(string stagingRoot, int maxConcurrency = 2) =>
        new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, maxConcurrency, stagingRoot);

    private static string[] SourceNames(TempDirectory dir) =>
        Directory.GetFiles(dir.Path).Select(f => Path.GetFileName(f)).ToArray();

    private static string CreateFolder(TempDirectory dir, string name, params string[] files)
    {
        string folder = Path.Combine(dir.Path, name);
        Directory.CreateDirectory(folder);
        foreach (string file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), $"content of {name}/{file}");
        }

        return folder;
    }

    /// <summary>File name to SHA-256 of its bytes; empty when the folder does not exist.</summary>
    private static Dictionary<string, string> Hashes(string folder) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder).ToDictionary(
                f => Path.GetFileName(f),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))),
                StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static Dictionary<string, string> ToDictionary(IEnumerable<KeyValuePair<string, string>> pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    private static string Listing(Dictionary<string, string> hashes) =>
        string.Join(", ", hashes.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value[..8]}"));

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };

    /// <summary>
    /// Records the most consume calls in flight at once. Every call waits until the cap is in flight
    /// together, so the peak reaches the cap whenever the runner allows that many.
    /// </summary>
    private sealed class PeakCountingOutput : IOutputNode
    {
        public const string RegisteredTypeKey = "PeakCounter";

        private readonly int _cap;
        private readonly Lock _sync = new();
        private readonly TaskCompletionSource _capReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _peak;
        private int _calls;

        public PeakCountingOutput(int cap) => _cap = cap;

        public string TypeKey => RegisteredTypeKey;

        public int Peak
        {
            get
            {
                lock (_sync)
                {
                    return _peak;
                }
            }
        }

        public int Calls
        {
            get
            {
                lock (_sync)
                {
                    return _calls;
                }
            }
        }

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public DestinationClaim? ClaimDestination(FileJob job, RunContext context) => null;

        public async Task ConsumeAsync(FileJob job, DestinationClaim? claim, RunContext context, CancellationToken ct = default)
        {
            lock (_sync)
            {
                _calls++;
                _inFlight++;
                _peak = Math.Max(_peak, _inFlight);
                if (_inFlight >= _cap)
                {
                    _capReached.TrySetResult();
                }
            }

            await _capReached.Task.WaitAsync(ct);

            // Leave asynchronously, so a loop without a cap would start the next call while this one still counts.
            await Task.Yield();
            lock (_sync)
            {
                _inFlight--;
            }
        }
    }

    /// <summary>A buffered transform whose flush adds a job it was never given, or leaves out the last one.</summary>
    private sealed class MisbehavingBufferNode : ITransformNode, IBufferedTransformNode
    {
        public const string RegisteredTypeKey = "MisbehavingBuffer";

        private readonly bool _addsAJob;
        private readonly List<FileJob> _buffer = [];

        public MisbehavingBufferNode(bool addsAJob) => _addsAJob = addsAJob;

        public string TypeKey => RegisteredTypeKey;

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            _buffer.Add(job);
            return Task.FromResult(Enumerable.Empty<FileJob>());
        }

        public Task<IEnumerable<FileJob>> FlushAsync(RunContext context, CancellationToken ct = default)
        {
            FileJob first = _buffer[0];
            List<FileJob> flushed = _addsAJob
                ? [.. _buffer, new FileJob { OriginalPath = first.OriginalPath, ContentPath = first.ContentPath, CurrentPath = Path.Combine(first.DirectoryName, "extra-" + first.FileName) }]
                : _buffer.Take(_buffer.Count - 1).ToList();
            return Task.FromResult<IEnumerable<FileJob>>(flushed);
        }
    }

    /// <summary>Returns each job together with a branch that carries its own staged copy of the content, as variant-&lt;name&gt;.</summary>
    private sealed class VariantTransform : ITransformNode
    {
        public const string RegisteredTypeKey = "Variant";

        public string TypeKey => RegisteredTypeKey;

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            FileJob variant = context.Branch(job);
            string staged = context.NewStagingPath(Path.GetExtension(job.ContentPath));
            await File.WriteAllBytesAsync(staged, await File.ReadAllBytesAsync(job.ContentPath, ct), ct);
            context.SetContent(variant, staged);
            variant.CurrentPath = Path.Combine(job.DirectoryName, "variant-" + job.FileName);
            return [job, variant];
        }
    }

    /// <summary>Returns each job together with an extra job built by hand rather than by the run.</summary>
    private sealed class HandBuiltExtraTransform : ITransformNode
    {
        public const string RegisteredTypeKey = "HandBuiltExtra";

        public string TypeKey => RegisteredTypeKey;

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            var extra = new FileJob
            {
                OriginalPath = job.OriginalPath,
                ContentPath = job.ContentPath,
                CurrentPath = Path.Combine(job.DirectoryName, "extra-" + job.FileName),
            };
            return Task.FromResult<IEnumerable<FileJob>>([job, extra]);
        }
    }
}
