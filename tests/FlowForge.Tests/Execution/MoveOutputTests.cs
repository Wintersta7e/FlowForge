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
}
