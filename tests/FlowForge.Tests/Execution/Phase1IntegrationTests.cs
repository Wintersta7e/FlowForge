using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using Serilog;

namespace FlowForge.Tests.Execution;

public class Phase1IntegrationTests
{
    private static PipelineRunner CreateRunner()
    {
        NodeRegistry registry = NodeRegistry.CreateDefault();
        ILogger logger = new LoggerConfiguration().CreateLogger();
        return new PipelineRunner(registry, logger, maxConcurrency: 2);
    }

    [Fact]
    public async Task MetadataExtract_to_RenamePattern_uses_file_metadata()
    {
        using var dir = new TempDirectory();
        TestFileFactory.CreateTestImages(dir.Path, "photo1.jpg", "photo2.jpg", "photo3.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Metadata Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.jpg" })
            .AddTransform("MetadataExtract", new { keys = new[] { "File:SizeBytes" } })
            .AddTransform("RenamePattern", new { pattern = "{meta:File:SizeBytes}_{name}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(3);
        result.Failed.Should().Be(0);

        string[] outputFiles = dir.OutputFiles;
        outputFiles.Should().HaveCount(3);
        // Each file should have a size prefix
        outputFiles.Should().OnlyContain(f => f.Contains("_photo"));
    }

    [Fact]
    public async Task Filter_then_rename_drops_non_matching_files()
    {
        using var dir = new TempDirectory();
        TestFileFactory.CreateTestImages(dir.Path, "photo1.jpg", "photo2.jpg");
        dir.CreateFiles("readme.txt", "notes.md");

        PipelineGraph pipeline = PipelineBuilder
            .New("Filter Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*" })
            .AddTransform("Filter", new
            {
                conditions = new[]
                {
                    new { field = "extension", @operator = "equals", value = ".jpg" }
                }
            })
            .AddTransform("RenamePattern", new { pattern = "img_{counter:000}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(2);
        dir.OutputFiles.Should().HaveCount(2);
        dir.OutputFiles.Should().Contain("img_001.jpg");
        dir.OutputFiles.Should().Contain("img_002.jpg");
    }

    [Fact]
    public async Task Sort_then_rename_orders_files_before_renaming()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("charlie.txt", "alpha.txt", "bravo.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Sort Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.txt" })
            .AddTransform("Sort", new { field = "filename", direction = "asc" })
            .AddTransform("RenamePattern", new { pattern = "{counter:000}{ext}" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(3);
        dir.OutputFiles.Should().Contain("001.txt"); // alpha
        dir.OutputFiles.Should().Contain("002.txt"); // bravo
        dir.OutputFiles.Should().Contain("003.txt"); // charlie
    }

    [Fact]
    public async Task ImageResize_in_pipeline_resizes_and_copies()
    {
        using var dir = new TempDirectory();
        TestFileFactory.CreateTestImages(dir.Path, "big1.jpg", "big2.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Image Resize Pipeline")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.jpg" })
            .AddTransform("ImageResize", new { width = 50, mode = "max" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(2);
        dir.OutputFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task RenameRegex_replaces_pattern_in_filename()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("IMG_2024_001.jpg", "IMG_2024_002.jpg");

        PipelineGraph pipeline = PipelineBuilder
            .New("Regex Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.jpg" })
            .AddTransform("RenameRegex", new
            {
                pattern = @"IMG_(\d{4})_(\d{3})",
                replacement = "Photo_$1-$2",
                scope = "filename"
            })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(2);
        dir.OutputFiles.Should().Contain("Photo_2024-001.jpg");
        dir.OutputFiles.Should().Contain("Photo_2024-002.jpg");
    }

    [Fact]
    public async Task RenameAddAffix_adds_prefix_and_suffix()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("doc.txt", "notes.txt");

        PipelineGraph pipeline = PipelineBuilder
            .New("Affix Rename Test")
            .AddSource("FolderInput", new { path = dir.Path, filter = "*.txt" })
            .AddTransform("RenameAddAffix", new { prefix = "backup_", suffix = "_v2" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath, mode = "copy" })
            .Build();

        PipelineRunner runner = CreateRunner();
        ExecutionResult result = await runner.RunAsync(pipeline, dryRun: false);

        result.Succeeded.Should().Be(2);
        dir.OutputFiles.Should().Contain("backup_doc_v2.txt");
        dir.OutputFiles.Should().Contain("backup_notes_v2.txt");
    }
}
