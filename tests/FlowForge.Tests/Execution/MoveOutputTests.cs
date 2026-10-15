using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class MoveOutputTests
{
    private readonly ITestOutputHelper _output;

    public MoveOutputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task In_place_move_keeps_the_delivered_file()
    {
        using var dir = new TempDirectory();
        string data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        string file = Path.Combine(data, "f.txt");
        await File.WriteAllTextAsync(file, "IN PLACE");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = data })
            .AddTransform("RenamePattern", new { pattern = "{name}{ext}" })
            .AddOutput("FolderOutput", new { path = data, mode = "move", overwrite = true })
            .Build();

        ExecutionResult result = await new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot).RunAsync(graph);

        string listing = $"succeeded={result.Succeeded} failed={result.Failed} data: "
            + string.Join(", ", Directory.GetFiles(data).Select(f => Path.GetFileName(f)));
        _output.WriteLine($"[move] {listing}");
        result.Succeeded.Should().Be(1, listing);
        File.Exists(file).Should().BeTrue(listing);
        (await File.ReadAllTextAsync(file)).Should().Be("IN PLACE", listing);
    }

    [Fact]
    public async Task Copy_and_move_outputs_both_deliver_before_the_original_is_deleted()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        string original = Path.Combine(input, "f.txt");
        await File.WriteAllTextAsync(original, "ORIGINAL");
        string copyOutput = Path.Combine(dir.Path, "outA");
        string moveOutput = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input });
        NodeDefinition copy = PipelineBuilder.Node("FolderOutput", new { path = copyOutput, mode = "copy" });
        NodeDefinition move = PipelineBuilder.Node("FolderOutput", new { path = moveOutput, mode = "move" });
        var graph = new PipelineGraph
        {
            Nodes = { source, copy, move },
            Connections = { Wire(source, copy), Wire(source, move) },
        };

        ExecutionResult result = await CreateRunner(dir).RunAsync(graph);

        string listing = Listing(result, input, copyOutput, moveOutput);
        _output.WriteLine($"[copy+move] {listing}");
        result.Succeeded.Should().Be(2, listing);
        (await File.ReadAllTextAsync(Path.Combine(copyOutput, "f.txt"))).Should().Be("ORIGINAL", listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "f.txt"))).Should().Be("ORIGINAL", listing);
        File.Exists(original).Should().BeFalse(listing);
    }

    [Fact]
    public async Task Failing_branch_keeps_the_original()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        string original = Path.Combine(input, "f.jpg");
        await File.WriteAllTextAsync(original, "NOT AN IMAGE");
        string moveOutput = Path.Combine(dir.Path, "outA");
        string convertOutput = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input });
        NodeDefinition move = PipelineBuilder.Node("FolderOutput", new { path = moveOutput, mode = "move" });
        NodeDefinition convert = PipelineBuilder.Node("ImageConvert", new { format = "png" });
        NodeDefinition converted = PipelineBuilder.Node("FolderOutput", new { path = convertOutput, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, move, convert, converted },
            Connections = { Wire(source, move), Wire(source, convert), Wire(convert, converted) },
        };

        ExecutionResult result = await CreateRunner(dir).RunAsync(graph);

        string listing = Listing(result, input, moveOutput, convertOutput);
        _output.WriteLine($"[failing-branch] {listing}");
        result.Succeeded.Should().Be(1, listing);
        result.Failed.Should().Be(1, listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "f.jpg"))).Should().Be("NOT AN IMAGE", listing);
        File.Exists(original).Should().BeTrue(listing);
        (await File.ReadAllTextAsync(original)).Should().Be("NOT AN IMAGE", listing);
    }

    private static PipelineRunner CreateRunner(TempDirectory dir) =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot);

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };

    private static string Listing(ExecutionResult result, params string[] folders) =>
        $"succeeded={result.Succeeded} failed={result.Failed} errors=[{string.Join("; ", result.Jobs.Select(j => j.ErrorMessage).OfType<string>())}] "
        + string.Join(" ", folders.Select(folder => Path.GetFileName(folder) + ": "
            + (Directory.Exists(folder) ? string.Join(", ", Directory.GetFiles(folder).Select(f => Path.GetFileName(f))) : "-")));
}
