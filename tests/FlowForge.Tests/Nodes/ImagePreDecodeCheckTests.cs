using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Nodes;

public class ImagePreDecodeCheckTests
{
    private const string Unresolved = "unresolved";
    private const string TooLarge = "too large";

    private readonly ITestOutputHelper _output;

    public ImagePreDecodeCheckTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("ImageResize", "photo.jpe", Unresolved)]
    [InlineData("ImageCompress", "photo.bmp", Unresolved)]
    [InlineData("ImageResize", "big.jpg", TooLarge)]
    [InlineData("ImageCompress", "big.jpg", TooLarge)]
    [InlineData("ImageConvert", "big.jpg", TooLarge)]
    public async Task Dry_run_reports_the_same_refusal_as_live(string typeKey, string fileName, string kind)
    {
        using var dir = new TempDirectory();
        string filePath = Path.Combine(dir.Path, fileName);
        switch (kind)
        {
            case TooLarge:
                await using (var stream = new FileStream(filePath, FileMode.CreateNew))
                {
                    stream.SetLength(501L * 1024 * 1024);
                }

                break;
            case Unresolved when fileName.EndsWith(".bmp", StringComparison.Ordinal):
                TestFileFactory.CreateTestBmp(filePath);
                break;
            default:
                string jpeg = Path.Combine(dir.Path, "source.jpg");
                TestFileFactory.CreateTestImage(jpeg);
                File.Move(jpeg, filePath);
                break;
        }

        FileJob live = NewJob(filePath);
        FileJob dry = NewJob(filePath);
        using (RunContext liveContext = TestRunContext.Live(dir.StagingRoot))
        {
            await CreateNode(typeKey).TransformAsync(live, liveContext);
        }

        using (RunContext dryContext = TestRunContext.Dry())
        {
            await CreateNode(typeKey).TransformAsync(dry, dryContext);
        }

        _output.WriteLine($"[pre-decode] {typeKey} {fileName} ({kind}): live={live.Status} '{live.ErrorMessage}' dry={dry.Status} '{dry.ErrorMessage}'");
        live.Status.Should().Be(FileJobStatus.Failed);
        dry.Status.Should().Be(FileJobStatus.Failed);
        live.ErrorMessage.Should().StartWith(typeKey + ": ");
        dry.ErrorMessage.Should().Be(live.ErrorMessage);
        dry.NodeLog.Should().Equal(live.NodeLog);
        Directory.Exists(dir.StagingRoot).Should().BeFalse("no check reads content or stages a file");
    }

    private static FileJob NewJob(string filePath) =>
        new() { OriginalPath = filePath, CurrentPath = filePath, ContentPath = filePath };

    private static ITransformNode CreateNode(string typeKey)
    {
        (ITransformNode node, object config) = typeKey switch
        {
            "ImageResize" => ((ITransformNode)new ImageResizeNode(NullLogger<ImageResizeNode>.Instance), (object)new { width = 50 }),
            "ImageCompress" => (new ImageCompressNode(NullLogger<ImageCompressNode>.Instance), new { quality = 80 }),
            _ => (new ImageConvertNode(NullLogger<ImageConvertNode>.Instance), new { format = "png" }),
        };

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(config));
        node.Configure(doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal));
        return node;
    }
}
