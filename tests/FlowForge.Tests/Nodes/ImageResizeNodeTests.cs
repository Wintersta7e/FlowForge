using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using SixLabors.ImageSharp;

namespace FlowForge.Tests.Nodes;

public class ImageResizeNodeTests
{
    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Resize_by_max_width_maintains_aspect()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "wide.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);

        var node = new ImageResizeNode();
        node.Configure(MakeConfig(new { width = 400, mode = "max" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        using Image resized = await Image.LoadAsync(filePath);
        resized.Width.Should().Be(400);
        resized.Height.Should().Be(300); // Aspect ratio maintained
    }

    [Fact]
    public async Task Resize_by_max_height()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "tall.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 600, height: 800);

        var node = new ImageResizeNode();
        node.Configure(MakeConfig(new { height = 400, mode = "max" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, dryRun: false);
        result.Should().HaveCount(1);

        using Image resized = await Image.LoadAsync(filePath);
        resized.Height.Should().Be(400);
        resized.Width.Should().Be(300); // Aspect ratio maintained
    }

    [Fact]
    public async Task Resize_stretch_ignores_aspect()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "stretch.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);

        var node = new ImageResizeNode();
        node.Configure(MakeConfig(new { width = 200, height = 200, mode = "stretch" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        await node.TransformAsync(job, dryRun: false);

        using Image resized = await Image.LoadAsync(filePath);
        resized.Width.Should().Be(200);
        resized.Height.Should().Be(200);
    }

    [Fact]
    public async Task DryRun_does_not_modify_image()
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, "notouch.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 800, height: 600);
        long originalSize = new FileInfo(filePath).Length;

        var node = new ImageResizeNode();
        node.Configure(MakeConfig(new { width = 100 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath
        };

        await node.TransformAsync(job, dryRun: true);

        using Image unchanged = await Image.LoadAsync(filePath);
        unchanged.Width.Should().Be(800);
        unchanged.Height.Should().Be(600);
    }

    [Fact]
    public void Missing_dimensions_throws()
    {
        var node = new ImageResizeNode();
        Action act = () => node.Configure(MakeConfig(new { mode = "max" }));
        act.Should().Throw<NodeConfigurationException>();
    }
}
