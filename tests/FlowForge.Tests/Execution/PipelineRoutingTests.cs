using FlowForge.Core.Execution;
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

        Func<Task> act = () => CreateRunner().RunAsync(graph, dryRun: false);

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

        Func<Task> act = () => CreateRunner().RunAsync(graph, dryRun: false);

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

        Func<Task> act = () => CreateRunner().RunAsync(graph, dryRun: false);

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

        ExecutionResult result = await CreateRunner().RunAsync(graph, dryRun: false);

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

                await CreateRunner(concurrency).RunAsync(graph, dryRun: false);

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

    private static PipelineRunner CreateRunner(int maxConcurrency = 2) =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, maxConcurrency);

    private static string[] SourceNames(TempDirectory dir) =>
        Directory.GetFiles(dir.Path).Select(f => Path.GetFileName(f)).ToArray();

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };
}
