using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class DryRunTreeAuditTests
{
    private const string SamplePrefix = "sample/";
    private const string TemplatePrefix = "template/";

    private readonly ITestOutputHelper _output;

    public DryRunTreeAuditTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, string> Pipelines()
    {
        string samplesDir = RepoRoot.Combine("samples");
        string[] samples = Directory.Exists(samplesDir)
            ? Directory.GetFiles(samplesDir, "*.ffpipe").Select(Path.GetFileNameWithoutExtension)
                .OfType<string>().Order(StringComparer.Ordinal).ToArray()
            : [];
        if (samples.Length == 0)
        {
            throw new InvalidOperationException($"No .ffpipe sample found under '{samplesDir}'.");
        }

        var data = new TheoryData<string, string>();
        IEnumerable<string> pipelines = samples.Select(stem => SamplePrefix + stem)
            .Concat(PipelineTemplateLibrary.Templates.Select(t => TemplatePrefix + t.Id));
        foreach (string pipeline in pipelines)
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
        CreateFixture(Path.Combine(root, "input"));
        PipelineGraph graph = await LoadAsync(pipeline);
        PointAt(graph, Path.Combine(root, "input"), Path.Combine(root, "output"));

        if (string.Equals(state, "prepopulated", StringComparison.Ordinal))
        {
            string scratch = Path.Combine(dir.Path, "scratch");
            CreateFixture(Path.Combine(scratch, "input"));
            PipelineGraph live = await LoadAsync(pipeline);
            PointAt(live, Path.Combine(scratch, "input"), Path.Combine(scratch, "output"));
            await CreateRunner().RunAsync(live);

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

            foreach (NodeDefinition node in graph.Nodes.Where(n => IsType(n, "FolderOutput")))
            {
                node.Config["overwrite"] = JsonSerializer.SerializeToElement(true);
                node.Config["enableBackup"] = JsonSerializer.SerializeToElement(true);
            }
        }

        var before = TreeSnapshot.Take(root);
        await CreateRunner().RunAsync(graph, dryRun: true);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(root), _output, $"{pipeline}/{state}");
    }

    private static PipelineRunner CreateRunner() =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance);

    private static async Task<PipelineGraph> LoadAsync(string pipeline) =>
        pipeline.StartsWith(SamplePrefix, StringComparison.Ordinal)
            ? await PipelineSerializer.LoadAsync(RepoRoot.Combine("samples", pipeline[SamplePrefix.Length..] + ".ffpipe"))
            : PipelineTemplateLibrary.CreateFromTemplate(pipeline[TemplatePrefix.Length..]);

    private static void CreateFixture(string input)
    {
        TestFileFactory.CreateTestImage(Path.Combine(input, "holiday.jpg"));
        TestFileFactory.CreateTestPng(Path.Combine(input, "scan.png"));
        File.WriteAllText(Path.Combine(input, "notes.txt"), "notes");
        TestFileFactory.CreateTestImage(Path.Combine(input, "sub", "inner.jpg"));
        File.WriteAllText(Path.Combine(input, "sub", "readme.txt"), "readme");
    }

    private static void PointAt(PipelineGraph graph, string input, string output)
    {
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (IsType(node, "FolderInput"))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement(input);
            }
            else if (IsType(node, "FolderOutput"))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement(output);
            }
        }
    }

    private static bool IsType(NodeDefinition node, string typeKey) =>
        string.Equals(node.TypeKey, typeKey, StringComparison.Ordinal);
}
