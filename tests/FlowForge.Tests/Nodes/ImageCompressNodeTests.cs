using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Transforms;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Nodes;

public class ImageCompressNodeTests
{
    private readonly ITestOutputHelper _output;

    public ImageCompressNodeTests(ITestOutputHelper output) => _output = output;

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
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "photo.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 200, height: 200);
        long originalSize = new FileInfo(filePath).Length;
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);

        job.ContentPath.Should().NotBe(filePath);
        byte[] compressed = await File.ReadAllBytesAsync(job.ContentPath);
        compressed.Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 }, Magic(compressed));
        compressed.LongLength.Should().BeLessThanOrEqualTo(originalSize);
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task Compress_png_quality50()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "image.png");
        TestFileFactory.CreateTestPng(filePath, width: 200, height: 200);
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);

        job.ContentPath.Should().NotBe(filePath);
        byte[] compressed = await File.ReadAllBytesAsync(job.ContentPath);
        compressed.Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, Magic(compressed));
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task Quality_boundary_minimum_1()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "min.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 1 }));
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);
        job.ContentPath.Should().NotBe(filePath);
        (await File.ReadAllBytesAsync(job.ContentPath)).Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 });
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
    }

    [Fact]
    public async Task Quality_boundary_maximum_100()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "max.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 100 }));
        byte[] sourceBytes = await File.ReadAllBytesAsync(filePath);

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().HaveCount(1);
        job.ContentPath.Should().NotBe(filePath);
        (await File.ReadAllBytesAsync(job.ContentPath)).Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 });
        (await File.ReadAllBytesAsync(filePath)).Should().Equal(sourceBytes, "the source is never rewritten");
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
    public async Task Format_override_delivers_bytes_matching_the_new_extension()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "input");
        Directory.CreateDirectory(input);
        TestFileFactory.CreateTestPng(Path.Combine(input, "x.png"), width: 100, height: 100);
        var runner = new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot);

        ExecutionResult result = await runner.RunAsync(PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input })
            .AddTransform("ImageCompress", new { quality = 75, format = "webp" })
            .AddOutput("FolderOutput", new { path = dir.OutputPath })
            .Build());

        (int scanned, IReadOnlyList<string> mismatches, string because) =
            await ScanMagicAsync(Directory.EnumerateFiles(dir.OutputPath).Select(path => (path, path)));
        because = string.Join("; ", [because, .. result.Jobs.Select(j => j.ErrorMessage)]);

        result.Succeeded.Should().Be(1, because);
        dir.OutputFiles.Should().Equal(["x.webp"], because);
        byte[] delivered = await File.ReadAllBytesAsync(Path.Combine(dir.OutputPath, "x.webp"));
        delivered.Take(4).Should().Equal("RIFF"u8.ToArray(), because);
        delivered.Skip(8).Take(4).Should().Equal("WEBP"u8.ToArray(), because);
        scanned.Should().BePositive(because);
        mismatches.Should().BeEmpty(because);
    }

    [Fact]
    public async Task Cross_format_override_delivers_matching_magic()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        using RunContext dry = TestRunContext.Dry();
        string filePath = Path.Combine(dir.Path, "pic.png");
        TestFileFactory.CreateTestPng(filePath, width: 40, height: 40);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 80, format = "jpg" }));

        var job = new FileJob { OriginalPath = filePath, CurrentPath = filePath, ContentPath = filePath };
        await node.TransformAsync(job, ctx);
        var dryJob = new FileJob { OriginalPath = filePath, CurrentPath = filePath, ContentPath = filePath };
        await node.TransformAsync(dryJob, dry);

        (int scanned, IReadOnlyList<string> mismatches, string because) =
            await ScanMagicAsync([(job.CurrentPath, job.ContentPath), (filePath, filePath)]);
        because = string.Join("; ", because, job.ErrorMessage, dryJob.ErrorMessage);

        job.Status.Should().NotBe(FileJobStatus.Failed, because);
        Path.GetFileName(job.CurrentPath).Should().Be("pic.jpg", because);
        (await File.ReadAllBytesAsync(job.ContentPath)).Take(2).Should().Equal(new byte[] { 0xFF, 0xD8 }, because);
        (await File.ReadAllBytesAsync(filePath)).Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, because);
        dryJob.Status.Should().NotBe(FileJobStatus.Failed, because);
        Path.GetFileName(dryJob.CurrentPath).Should().Be("pic.jpg", because);
        scanned.Should().Be(2, because);
        mismatches.Should().BeEmpty(because);
    }

    /// <summary>
    /// Pairs each name's image extension with the magic bytes of its content and prints
    /// <c>scanned=n mismatches=m</c>; the returned reason carries the same line and every mismatch.
    /// </summary>
    private async Task<(int Scanned, IReadOnlyList<string> Mismatches, string Because)> ScanMagicAsync(
        IEnumerable<(string Name, string ContentPath)> files)
    {
        int scanned = 0;
        var mismatches = new List<string>();
        foreach ((string name, string contentPath) in files)
        {
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp"))
            {
                continue;
            }

            scanned++;
            byte[] content = await File.ReadAllBytesAsync(contentPath);
            bool matches = extension switch
            {
                ".png" => content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
                ".webp" => content.Length >= 12 && content.AsSpan(0, 4).SequenceEqual("RIFF"u8) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                _ => content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
            };
            if (!matches)
            {
                mismatches.Add($"{Path.GetFileName(name)} magic {Magic(content)}");
            }
        }

        string summary = $"scanned={scanned} mismatches={mismatches.Count}";
        _output.WriteLine(summary);
        mismatches.ForEach(_output.WriteLine);
        return (scanned, mismatches, string.Join("; ", [summary, .. mismatches]));
    }

    private static string Magic(byte[] content) => BitConverter.ToString(content, 0, Math.Min(4, content.Length)).Replace('-', ' ');

    [Fact]
    public async Task Unsupported_format_sets_failed_status()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "unsupported.bmp");
        TestFileFactory.CreateTestBmp(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50, format = "bmp" }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
        result.Should().ContainSingle();
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().Contain("unsupported format").And.Contain("bmp");
    }

    [Fact]
    public async Task Unsupported_extension_without_format_override_sets_failed()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        // Create a .bmp file (valid image but unsupported format for compression)
        string filePath = Path.Combine(dir.Path, "image.bmp");
        TestFileFactory.CreateTestBmp(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        // No format override — relies on file extension, which is .bmp (unsupported)
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);
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
            CurrentPath = filePath,
            ContentPath = filePath
        };

        await node.TransformAsync(job, TestRunContext.Dry());

        new FileInfo(filePath).Length.Should().Be(originalSize);
        job.NodeLog.Should().ContainSingle(log => log.Contains("would compress"));
    }

    [Fact]
    public async Task CancellationToken_cancelled_throws_OperationCanceledException()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "cancel.jpg");
        TestFileFactory.CreateTestImage(filePath, width: 100, height: 100);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

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

    [Fact(Timeout = 10000)]
    public async Task BigTiff_input_fails_without_decoding()
    {
        using var dir = new TempDirectory();
        using RunContext ctx = TestRunContext.Live(dir.StagingRoot);
        string filePath = Path.Combine(dir.Path, "header.tiff");
        TestFileFactory.CreateBigTiffHeader(filePath);
        byte[] originalBytes = File.ReadAllBytes(filePath);

        var node = new ImageCompressNode(NullLogger<ImageCompressNode>.Instance);
        node.Configure(MakeConfig(new { quality = 50 }));

        var job = new FileJob
        {
            OriginalPath = filePath,
            CurrentPath = filePath,
            ContentPath = filePath
        };

        IEnumerable<FileJob> result = await node.TransformAsync(job, ctx);

        result.Should().ContainSingle().Which.Should().BeSameAs(job);
        job.Status.Should().Be(FileJobStatus.Failed);
        job.ErrorMessage.Should().MatchRegex("^ImageCompress: .+");
        Directory.GetFiles(dir.Path).Should().Equal(filePath);
        File.ReadAllBytes(filePath).Should().Equal(originalBytes);
    }
}
