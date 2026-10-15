using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class PipelineShapeRefusalTests
{
    private readonly ITestOutputHelper _output;

    public PipelineShapeRefusalTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Unconnected_transform_beside_a_chain_is_refused()
    {
        using var dir = new TempDirectory();
        string input = CreateInput(dir, "a.txt", "b.txt");
        string output = Path.Combine(dir.Path, "out");
        NodeDefinition source = Node("FolderInput", new { path = input });
        NodeDefinition sink = Node("FolderOutput", new { path = output, mode = "copy" });
        NodeDefinition stray = Node("RenameAddAffix", new { prefix = "x_" });
        var graph = new PipelineGraph { Nodes = { source, sink, stray }, Connections = { Wire(source, sink) } };

        var before = TreeSnapshot.Take(input);
        Exception? refusal = await Record.ExceptionAsync(() => CreateRunner(dir.StagingRoot).RunAsync(graph));
        string listing = "in: " + string.Join(", ", Directory.GetFiles(input).Select(Path.GetFileName));
        _output.WriteLine(listing);

        refusal.Should().BeOfType<InvalidOperationException>(listing)
            .Which.Message.Should().Contain("RenameAddAffix", listing).And.Contain(stray.Id.ToString(), listing);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(input), _output, "in");
        Directory.Exists(output).Should().BeFalse();
    }

    private static PipelineRunner CreateRunner(string stagingRoot) =>
        new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: stagingRoot);

    private static string CreateInput(TempDirectory dir, params string[] names)
    {
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(input, name), $"content of {name}");
        }

        return input;
    }

    private static NodeDefinition Node(string typeKey, object config) => new()
    {
        TypeKey = typeKey,
        Config = JsonSerializer.SerializeToElement(config).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal),
    };

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };
}
