using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;

namespace FlowForge.Tests.Nodes;

public class ImageResizeNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Resize_by_max_width_maintains_aspect()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "wide.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 400, mode = "max" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);

        using Image resized = await Image.LoadAsync(job.ContentPath);
        resized.Width.Should().Be(400);
        resized.Height.Should().Be(300); // Aspect ratio maintained
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task Resize_by_max_height()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "tall.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 600, height: 800);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { height = 400, mode = "max" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);

        using Image resized = await Image.LoadAsync(job.ContentPath);
        resized.Height.Should().Be(400);
        resized.Width.Should().Be(300); // Aspect ratio maintained
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task Resize_stretch_ignores_aspect()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "stretch.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 200, height = 200, mode = "stretch" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        await node.TransformAsync(job, ctx);

        using Image resized = await Image.LoadAsync(job.ContentPath);
        resized.Width.Should().Be(200);
        resized.Height.Should().Be(200);
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task DryRun_does_not_modify_image()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "notouch.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);
        long originalSize = new FileInfo(filePath).Length;

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 100 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        await node.TransformAsync(job, TestRunContext.Dry());

        using Image unchanged = await Image.LoadAsync(filePath);
        unchanged.Width.Should().Be(800);
        unchanged.Height.Should().Be(600);
    }

    [Fact]
    public void Missing_dimensions_throws()
    {
        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { mode = "max" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "cancel.png");
        TestFileFactory.CreateTestPng(filePath, width: 100, height: 100);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, ctx, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Configure_width_exceeds_max_throws()
    {
        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { width = 50000 }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task MaintainAspect_false_forces_stretch()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "stretch_test.png");
        TestFileFactory.CreateTestImage(filePath, 200, 100);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 50, height = 50, mode = "max", maintainAspect = false }));

        var job = new FileJob { OriginalPath = filePath, CurrentPath = filePath, ContentPath = filePath };
        await node.TransformAsync(job, ctx);

        using var image = Image.Load(job.ContentPath);
        image.Width.Should().Be(50);
        image.Height.Should().Be(50);
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Theory]
    [InlineData("ccitt-g3.tiff")]
    [InlineData("ccitt-g4.tiff")]
    public async Task Resize_of_ccitt_tiff_writes_lzw(string fixture)
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, fixture);
        File.Copy(TestFileFactory.ImageFixturePath(fixture), filePath);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 32, mode = "max" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        await node.TransformAsync(job, ctx);

        job.Status.Should().NotBe(FileJobStatus.Failed);
        TiffTagReader.ReadCompression(job.ContentPath).Should().Be(5, "TIFF output is written as LZW");
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact(Timeout = 10000)]
    public async Task BigTiff_input_fails_without_decoding()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "header.tiff");
        TestFileFactory.CreateBigTiffHeader(filePath);
        byte[] originalBytes = File.ReadAllBytes(filePath);

        var node = new ImageResizeNode(NullLogger<ImageResizeNode>.Instance);
        node.Configure(MakeConfig(new { width = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);

        result.Should().ContainSingle().Which.Should().BeSameAs(job);
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().MatchRegex("^ImageResize: .+");
        Directory.GetFiles(dir.Path).Should().Equal(filePath);
        File.ReadAllBytes(filePath).Should().Equal(originalBytes);
    }
}
