using System.Globalization;
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

    [Fact]
    public async Task Copy_and_move_outputs_from_one_source_are_refused()
    {
        using var dir = new TempDirectory();
        string input = CreateInput(dir, "a.txt");
        string copyOutput = Path.Combine(dir.Path, "outA");
        string moveOutput = Path.Combine(dir.Path, "outB");
        NodeDefinition source = Node("FolderInput", new { path = input });
        NodeDefinition copy = Node("FolderOutput", new { path = copyOutput, mode = "copy" });
        NodeDefinition move = Node("FolderOutput", new { path = moveOutput, mode = "move" });
        var graph = new PipelineGraph
        {
            Nodes = { source, copy, move },
            Connections = { Wire(source, copy), Wire(source, move) },
        };

        var before = TreeSnapshot.Take(input);
        ExecutionResult? result = null;
        Exception? refusal = await Record.ExceptionAsync(async () => result = await CreateRunner(dir.StagingRoot).RunAsync(graph));
        int copied = Directory.Exists(copyOutput) ? Directory.GetFiles(copyOutput).Length : 0;
        string listing = $"outA entries={copied} succeeded={result?.Succeeded.ToString(CultureInfo.InvariantCulture) ?? "-"}";
        _output.WriteLine(listing);

        refusal.Should().BeOfType<InvalidOperationException>(listing)
            .Which.Message.Should().Contain("FolderInput", listing).And.Contain(source.Id.ToString(), listing);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(input), _output, "in");
        Directory.Exists(copyOutput).Should().BeFalse();
        Directory.Exists(moveOutput).Should().BeFalse();
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
