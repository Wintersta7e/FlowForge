using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using Xunit.Abstractions;

namespace FlowForge.Tests.Nodes;

public class ImageConvertNodeTests
{
    private readonly ITestOutputHelper _output;

    public ImageConvertNodeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Convert_never_replaces_a_sibling()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        string jpgPath = Path.Combine(input, "photo.jpg");
        string pngPath = Path.Combine(input, "photo.png");
        TestFileFactory.CreateTestImage(jpgPath, width: 10, height: 10);
        TestFileFactory.CreateTestPng(pngPath, width: 20, height: 20);

        ExecutionResult result = await CreateRunner().RunAsync(ConvertToJpegGraph(input, Path.Combine(dir.Path, "out")));

        string jpg = Dimensions(jpgPath);
        string png = Dimensions(pngPath);
        _output.WriteLine($"photo.jpg {jpg}");
        _output.WriteLine($"photo.png {png}");
        string because = $"photo.jpg {jpg}, photo.png {png}";
        jpg.Should().Be("10x10", because);
        png.Should().Be("20x20", because);
        result.Succeeded.Should().Be(1, because);
        result.Failed.Should().Be(1, because);
        result.Jobs.Should().ContainSingle(j => j.Status == FileJobStatus.Failed, because)
            .Which.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Case_only_extension_change_keeps_the_converted_file()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        TestFileFactory.CreateTestImage(Path.Combine(input, "photo.JPG"), width: 10, height: 10);

        ExecutionResult result = await CreateRunner().RunAsync(ConvertToJpegGraph(input, Path.Combine(dir.Path, "out")));

        var names = Directory.GetFiles(input).Select(f => Path.GetFileName(f)).ToList();
        string listing = "in: " + string.Join(", ", names);
        _output.WriteLine(listing);
        string? converted = names.Find(n => string.Equals(n, "photo.jpg", StringComparison.OrdinalIgnoreCase));
        converted.Should().NotBeNull(listing);
        byte[] bytes = await File.ReadAllBytesAsync(Path.Combine(input, converted!));
        bytes.Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 }, listing);
        result.Jobs.Should().ContainSingle(listing).Which.Status.Should().Be(FileJobStatus.Succeeded, listing);
    }

    private static PipelineRunner CreateRunner() =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance);

    private static PipelineGraph ConvertToJpegGraph(string input, string output)
    {
        NodeDefinition source = Node("FolderInput", new { path = input });
        NodeDefinition convert = Node("ImageConvert", new { format = "jpg" });
        NodeDefinition sink = Node("FolderOutput", new { path = output });
        return new PipelineGraph
        {
            Nodes = { source, convert, sink },
            Connections = { Wire(source, convert), Wire(convert, sink) },
        };
    }

    private static NodeDefinition Node(string typeKey, object config) => new()
    {
        TypeKey = typeKey,
        Config = JsonSerializer.SerializeToElement(config).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal),
    };

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };

    /// <summary>Reads a JPEG's or PNG's dimensions with a library other than the one that wrote it.</summary>
    private static string Dimensions(string path)
    {
        if (!File.Exists(path))
        {
            return "missing";
        }

        IReadOnlyList<MetadataExtractor.Directory> directories = MetadataExtractor.ImageMetadataReader.ReadMetadata(path);
        JpegDirectory? jpeg = directories.OfType<JpegDirectory>().FirstOrDefault();
        return jpeg != null
            ? Size(jpeg, JpegDirectory.TagImageWidth, JpegDirectory.TagImageHeight)
            : Size(directories.OfType<PngDirectory>().First(), PngDirectory.TagImageWidth, PngDirectory.TagImageHeight);

        static string Size(MetadataExtractor.Directory directory, int widthTag, int heightTag) =>
            $"{MetadataExtractor.DirectoryExtensions.GetInt32(directory, widthTag)}x{MetadataExtractor.DirectoryExtensions.GetInt32(directory, heightTag)}";
    }

    private static Dictionary<string, JsonElement> MakeConfig(object config)
    {
        string json = JsonSerializer.Serialize(config);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    [Fact]
    public async Task Convert_jpeg_to_png_creates_valid_png()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "photo.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Live());
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

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "jpg" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Live());
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

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "webp" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Live());
        result.Should().HaveCount(1);

        string expectedPath = Path.Combine(dir.Path, "sample.webp");
        job.CurrentPath.Should().Be(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        new FileInfo(expectedPath).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Invalid_format_throws_NodeConfigurationException()
    {
        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { format = "gif" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Fact]
    public void Missing_format_throws_NodeConfigurationException()
    {
        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { unrelated = "value" }));
        act.Should().Throw<NodeConfigurationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Empty_or_whitespace_format_throws_friendly_configuration_error(string emptyFormat)
    {
        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        Action act = () => node.Configure(MakeConfig(new { format = emptyFormat }));
        act.Should().Throw<NodeConfigurationException>()
            .WithMessage("*Convert Format station*");
    }

    [Fact]
    public async Task DryRun_updates_path_but_does_not_convert()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "original.jpg");
        TestFileFactory.CreateTestImage(inputPath, width: 100, height: 100);
        long originalSize = new FileInfo(inputPath).Length;

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Dry());
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

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        await node.TransformAsync(job, TestRunContext.Live());

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

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "jpg" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        await node.TransformAsync(job, TestRunContext.Live());

        job.CurrentPath.Should().Be(inputPath);
        File.Exists(inputPath).Should().BeTrue();
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "cancel.png");
        TestFileFactory.CreateTestPng(inputPath, width: 100, height: 100);

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "jpg" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => node.TransformAsync(job, TestRunContext.Live(), ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("ccitt-g3.tiff")]
    [InlineData("ccitt-g4.tiff")]
    public async Task Convert_to_tiff_writes_lzw_for_ccitt_input(string fixture)
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, fixture);
        File.Copy(TestFileFactory.ImageFixturePath(fixture), inputPath);

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "tiff" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        await node.TransformAsync(job, TestRunContext.Live());

        job.Status.Should().NotBe(FileJobStatus.Failed);
        TiffTagReader.ReadCompression(job.CurrentPath).Should().Be(5, "TIFF output is written as LZW");
    }

    [Fact(Timeout = 10000)]
    public async Task BigTiff_input_fails_without_decoding()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "header.tiff");
        TestFileFactory.CreateBigTiffHeader(inputPath);
        byte[] originalBytes = File.ReadAllBytes(inputPath);

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Live());

        result.Should().ContainSingle().Which.Should().BeSameAs(job);
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().MatchRegex("^ImageConvert: .+");
        Directory.GetFiles(dir.Path).Should().Equal(inputPath);
        File.ReadAllBytes(inputPath).Should().Equal(originalBytes);
    }

    [Fact(Timeout = 10000)]
    public async Task Big_endian_BigTiff_input_fails_without_decoding()
    {
        using var dir = new TempDirectory();
        string inputPath = Path.Combine(dir.Path, "header.tiff");
        TestFileFactory.CreateBigTiffHeader(inputPath, bigEndian: true);
        byte[] originalBytes = File.ReadAllBytes(inputPath);

        var node = new ImageConvertNode(NullLogger<ImageConvertNode>.Instance);
        node.Configure(MakeConfig(new { format = "png" }));

        var job = new FileJob
        {
            OriginalPath = inputPath,
            CurrentPath = inputPath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, TestRunContext.Live());

        result.Should().ContainSingle().Which.Should().BeSameAs(job);
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().MatchRegex("^ImageConvert: .+");
        Directory.GetFiles(dir.Path).Should().Equal(inputPath);
        File.ReadAllBytes(inputPath).Should().Equal(originalBytes);
    }
}
