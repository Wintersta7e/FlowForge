using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class DryRunTreeAuditTests
{
    private readonly ITestOutputHelper _output;

    public DryRunTreeAuditTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, string> Pipelines()
    {
        var data = new TheoryData<string, string>();
        foreach (string pipeline in PreviewPipelineRows.Names())
        {
            data.Add(pipeline, "absent");
            data.Add(pipeline, "prepopulated");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pipelines))]
    public async Task Dry_run_leaves_every_entry_unchanged(string pipeline, string state)
    {
        using var dir = new TempDirectory();
        string root = Path.Combine(dir.Path, "root");
        PreviewPipelineRows.CreateFixture(Path.Combine(root, "input"));
        PipelineGraph graph = await PreviewPipelineRows.LoadAsync(pipeline, root);

        if (string.Equals(state, "prepopulated", StringComparison.Ordinal))
        {
            string scratch = Path.Combine(dir.Path, "scratch");
            PreviewPipelineRows.CreateFixture(Path.Combine(scratch, "input"));
            PipelineGraph live = await PreviewPipelineRows.LoadAsync(pipeline, scratch);
            await CreateRunner(dir.StagingRoot).RunAsync(live);

            string liveOutput = Path.Combine(scratch, "output");
            string[] produced = Directory.Exists(liveOutput)
                ? Directory.GetFiles(liveOutput, "*", SearchOption.AllDirectories)
                : [];
            _output.WriteLine($"live_outputs={produced.Length}");
            produced.Should().NotBeEmpty("a live run must produce the names the dry run meets");
            foreach (string file in produced)
            {
                string target = Path.Combine(root, "output", Path.GetRelativePath(liveOutput, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }

            foreach (NodeDefinition node in graph.Nodes.Where(n => string.Equals(n.TypeKey, "FolderOutput", StringComparison.Ordinal)))
            {
                node.Config["overwrite"] = JsonSerializer.SerializeToElement(true);
                node.Config["enableBackup"] = JsonSerializer.SerializeToElement(true);
            }
        }

        var before = TreeSnapshot.Take(root);
        await CreateRunner(dir.StagingRoot).RunAsync(graph, dryRun: true);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(root), _output, $"{pipeline}/{state}");
    }

    private static PipelineRunner CreateRunner(string stagingRoot) =>
        new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: stagingRoot);
}
