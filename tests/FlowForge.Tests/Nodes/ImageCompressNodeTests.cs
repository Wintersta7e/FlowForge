using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Nodes;

public class ImageCompressNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Compress_jpeg_quality50_reduces_or_maintains_size()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "photo.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 200, height: 200);
        long originalSize = new FileInfo(filePath).Length;

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        File.Exists(filePath).Should().BeTrue();
        long compressedSize = new FileInfo(filePath).Length;
        compressedSize.Should().BeLessThanOrEqualTo(originalSize);
    }

    [Fact]
    public async Task Compress_png_quality50()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "image.png");
        TestFileFactory.CreateTestPng(filePath, width: 200, height: 200);
        long originalSize = new FileInfo(filePath).Length;

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        File.Exists(filePath).Should().BeTrue();
        new FileInfo(filePath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Quality_boundary_minimum_1()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "min.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 1 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task Quality_boundary_maximum_100()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "max.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 100 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void Quality_zero_throws_NodeConfigurationException()
    {
        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { quality = 0 }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Quality_101_throws_NodeConfigurationException()
    {
        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { quality = 101 }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Missing_quality_throws_NodeConfigurationException()
    {
        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { format = "jpg" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task Format_override_uses_different_encoder()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "override.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 75, format = "webp" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public async Task Unsupported_format_sets_failed_status()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "unsupported.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50, format = "bmp" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("unsupported format").And.Contain("bmp");
    }

    [Fact]
    public async Task Unsupported_extension_without_format_override_sets_failed()
    {
        using var dir = new TempDirectory();
        // Create a .bmp file (valid image but unsupported format for compression)
        string filePath = Path.Combine(dir.Path, "image.bmp");
        TestFileFactory.CreateTestBmp(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        // No format override — relies on file extension, which is .bmp (unsupported)
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("unsupported format").And.Contain("bmp");
    }

    [Fact]
    public async Task DryRun_does_not_compress_and_logs_message()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "dryrun.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);
        long originalSize = new FileInfo(filePath).Length;

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        await node.TransformAsync(job, dryRun: true);

        new FileInfo(filePath).Length.Should().Be(originalSize);
        job.NodeLog.Should().ContainSingle(log => log.Contains("would compress"));
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "cancel.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, dryRun: false, ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
