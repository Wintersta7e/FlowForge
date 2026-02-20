using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using Serilog;

namespace FlowForge.Tests.Execution;

public class PipelineRunnerTests
{
    private static PipelineRunner CreateRunner()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        ILogger logger = new LoggerConfiguration().CreateLogger();
        return new PipelineRunner(registry, logger, maxConcurrency: 2);
    }

    [Fact]
    public async Task EndToEnd_rename_sequential_copies_to_output()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Sequential Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.jpg" })
            .AddTransform("RenamePattern", new { pattern = "{counter:000}{ext}", startIndex = 1 })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(5);
        result.Failed.Should().Be(0);
        result.TotalFiles.Should().Be(5);

        string[] outputFiles = dir.OutputFiles;
        outputFiles.Should().HaveCount(5);
        outputFiles.Should().Contain("001.jpg");
        outputFiles.Should().Contain("002.jpg");
        outputFiles.Should().Contain("003.jpg");
        outputFiles.Should().Contain("004.jpg");
        outputFiles.Should().Contain("005.jpg");
    }

    [Fact]
    public async Task DryRun_does_not_write_files()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("photo1.jpg", "photo2.jpg", "photo3.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Dry Run Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.jpg" })
            .AddTransform("RenamePattern", new { pattern = "renamed_{name}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: true);

        result.Succeeded.Should().Be(3);
        result.IsDryRun.Should().BeTrue();

        // Output folder should be empty (only the dir itself exists from TempDirectory setup)
        dir.OutputFiles.Should().BeEmpty();

        // Source files should still exist with original names
        File.Exists(Path.Combine(dir.Path, "photo1.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(dir.Path, "photo2.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(dir.Path, "photo3.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task Empty_graph_throws()
    {
        var pipeline = new PipelineGraph();
        PipelineRunner runner = CreateRunner();

        Func<Task> act = () => runner.RunAsync(pipeline);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no nodes*");
    }

    [Fact]
    public async Task Unknown_node_type_throws()
    {
        var pipeline = new PipelineGraph();
        pipeline.Nodes.Add(new NodeDefinition { TypeKey = "NonExistentNode" });

        PipelineRunner runner = CreateRunner();
        Func<Task> act = () => runner.RunAsync(pipeline);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unknown node type*");
    }

    [Fact]
    public async Task Cyclic_graph_throws()
    {
        var node1 = new NodeDefinition { TypeKey = "FolderInput" };
        var node2 = new NodeDefinition { TypeKey = "RenamePattern" };

        var pipeline = new PipelineGraph();
        pipeline.Nodes.Add(node1);
        pipeline.Nodes.Add(node2);
        pipeline.Connections.Add(new Connection { FromNode = node1.Id, ToNode = node2.Id });
        pipeline.Connections.Add(new Connection { FromNode = node2.Id, ToNode = node1.Id });

        PipelineRunner runner = CreateRunner();
        Func<Task> act = () => runner.RunAsync(pipeline);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cycle*");
    }

    [Fact]
    public async Task Move_mode_moves_files_from_source()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("file1.txt", "file2.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Move Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "move" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(2);
        dir.OutputFiles.Should().HaveCount(2);

        // Source files should be gone after move
        File.Exists(Path.Combine(dir.Path, "file1.txt")).Should().BeFalse();
        File.Exists(Path.Combine(dir.Path, "file2.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task Transform_failure_with_Failed_status_counted_in_results()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("safe.txt");

        // RenameRegex with path traversal replacement triggers Failed status
        PipelineGraph pipeline = PipelineBuilder
            .New("Failed Transform Test")
            .AddSource("FolderInput", new { path = dir.Path, recursive = false, filter = "*.txt" })
            .AddTransform("RenameRegex", new { pattern = @"safe\.txt", replacement = "../escaped.txt", scope = "fullpath" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: true);

        result.Failed.Should().Be(1, "path traversal should be counted as a failed job");
        result.Succeeded.Should().Be(0);
        result.TotalFiles.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_stops_execution()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.jpg", "b.jpg", "c.jpg", "d.jpg", "e.jpg",
                        "f.jpg", "g.jpg", "h.jpg", "i.jpg", "j.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Cancel Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.jpg" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        PipelineRunner runner = CreateRunner();
        Func<Task> act = () => runner.RunAsync(pipeline, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
