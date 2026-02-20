using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using SixLabors.ImageSharp;

namespace FlowForge.Tests.Nodes;

public class ImageConvertNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Convert_jpeg_to_png_creates_valid_png()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "photo.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        string expectedPath = Path.Combine(dir.Path, "photo.png");
        job.CurrentPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();

        using Image image = await Image.LoadAsync(expectedPath);
        image.Width.Should().Be(100);
        image.Height.Should().Be(100);
    }

    [Fact]
    public async Task Convert_png_to_jpeg_creates_valid_jpeg()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "graphic.png");
        TestFileFactory.CreateTestPng(inputPath, width: 80, height: 60);

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "jpg" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        string expectedPath = Path.Combine(dir.Path, "graphic.jpg");
        job.CurrentPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();

        using Image image = await Image.LoadAsync(expectedPath);
        image.Width.Should().Be(80);
        image.Height.Should().Be(60);
    }

    [Fact]
    public async Task Convert_to_webp_creates_webp_file()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "sample.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 50, height: 50);

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "webp" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        string expectedPath = Path.Combine(dir.Path, "sample.webp");
        job.CurrentPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        new FileInfo(expectedPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Invalid_format_throws_NodeConfigurationException()
    {
        var node = new ImageConvertNode();
        Action act = () => node.Configure(MakeConfig(new { format = "gif" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Missing_format_throws_NodeConfigurationException()
    {
        var node = new ImageConvertNode();
        Action act = () => node.Configure(MakeConfig(new { unrelated = "value" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public async Task DryRun_updates_path_but_does_not_convert()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "original.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);
        long originalSize = new FileInfo(inputPath).Length;

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: true);
        result.Should().HaveCount(1);

        string expectedPath = Path.Combine(dir.Path, "original.png");
        job.CurrentPath.Should().Be(expectedPath);
        job.NodeLog.Should().Contain(log => log.Contains("would convert"));

        File.Exists(inputPath).Should().BeTrue();
        new FileInfo(inputPath).Length.Should().Be(originalSize);
        File.Exists(expectedPath).Should().BeFalse();
    }

    [Fact]
    public async Task Original_file_deleted_when_extension_changes()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "delete_me.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        await node.TransformAsync(job, dryRun: false);

        string convertedPath = Path.Combine(dir.Path, "delete_me.png");
        File.Exists(convertedPath).Should().BeTrue();
        File.Exists(inputPath).Should().BeFalse();
    }

    [Fact]
    public async Task Same_format_conversion_does_not_delete_original()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "keep_me.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);

        var node = new ImageConvertNode();
        node.Configure(MakeConfig(new { format = "jpg" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        await node.TransformAsync(job, dryRun: false);

        job.CurrentPath.Should().Be(inputPath);
        File.Exists(inputPath).Should().BeTrue();
    }
}
