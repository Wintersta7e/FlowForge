using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class SourceTreeUnchangedTests
{
    private readonly ITestOutputHelper _output;

    public SourceTreeUnchangedTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [MemberData(nameof(PreviewPipelineRows.SamplesAndTemplates), MemberType = typeof(PreviewPipelineRows))]
    public async Task Live_run_leaves_the_source_tree_byte_identical(string pipeline)
    {
        using var dir = new TempDirectory();
        string root = Path.Combine(dir.Path, "root");
        PreviewPipelineRows.CreateFixture(Path.Combine(root, "input"));
        string staging = Path.Combine(root, "staging");
        Directory.CreateDirectory(Path.Combine(root, "output"));
        Directory.CreateDirectory(staging);
        PipelineGraph graph = await PreviewPipelineRows.LoadAsync(pipeline, root);

        var before = TreeSnapshot.Take(root, "output", "staging");
        var runner = new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: staging);
        ExecutionResult result = await runner.RunAsync(graph);

        _output.WriteLine($"[run] {pipeline} succeeded={result.Succeeded} failed={result.Failed} skipped={result.Skipped}");
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(root, "output", "staging"), _output, pipeline);
        string[] leftovers = Directory.GetFileSystemEntries(staging);
        _output.WriteLine($"[staging] entries={leftovers.Length}");
        leftovers.Should().BeEmpty();
    }
}
