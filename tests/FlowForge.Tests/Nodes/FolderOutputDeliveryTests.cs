using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Outputs;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Nodes;

public class FolderOutputDeliveryTests
{
    private const int CopyBlock = 81920;
    private static readonly byte[] PreviousBytes = Encoding.UTF8.GetBytes("PREVIOUS");

    private readonly ITestOutputHelper _output;

    public FolderOutputDeliveryTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Cancelled_overwrite_keeps_the_previous_destination()
    {
        using var dir = new TempDirectory();
        string output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(output);
        using var cts = new CancellationTokenSource();

        string source = "/dev/zero";
        NamedPipeServerStream? pipe = null;
        Task writer = Task.CompletedTask;
        if (OperatingSystem.IsWindows())
        {
            string name = "flowforge-" + Guid.NewGuid().ToString("N");
            pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            writer = WriteZerosUntilCancelledAsync(pipe, cts.Token);
            source = $@"\\.\pipe\{name}";
        }

        string destination = Path.Combine(output, Path.GetFileName(source));
        await File.WriteAllBytesAsync(destination, PreviousBytes);
        FolderOutputNode node = CreateNode(new { path = output, overwrite = true });
        var job = new FileJob { OriginalPath = source, CurrentPath = source };

        cts.CancelAfter(TimeSpan.FromMilliseconds(300));
        Exception? thrown = await Record.ExceptionAsync(() => node.ConsumeAsync(job, dryRun: false, cts.Token));
        await writer;
        pipe?.Dispose();

        string because = await DescribeAsync(source, thrown, destination, output);
        thrown.Should().BeAssignableTo<OperationCanceledException>(because);
        new FileInfo(destination).Length.Should().Be(PreviousBytes.Length, because);
        (await File.ReadAllBytesAsync(destination)).Should().Equal(PreviousBytes, because);
        Directory.GetFileSystemEntries(output).Should().ContainSingle(because);
    }

    [Fact]
    public async Task Read_error_keeps_the_previous_destination()
    {
        using var dir = new TempDirectory();
        string output = Path.Combine(dir.Path, "out");
        Directory.CreateDirectory(output);

        string source = "/proc/self/mem";
        if (OperatingSystem.IsWindows())
        {
            source = Path.Combine(dir.Path, "locked.bin");
            await File.WriteAllBytesAsync(source, new byte[4 * CopyBlock]);
        }

        // On Windows a second handle holds everything after the first block, so the copy's second read fails.
        using FileStream? locker = OperatingSystem.IsWindows()
            ? new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        if (OperatingSystem.IsWindows())
        {
            locker!.Lock(CopyBlock, 3 * CopyBlock);
        }

        string destination = Path.Combine(output, Path.GetFileName(source));
        await File.WriteAllBytesAsync(destination, PreviousBytes);
        FolderOutputNode node = CreateNode(new { path = output, overwrite = true });
        var job = new FileJob { OriginalPath = source, CurrentPath = source };

        Exception? thrown = await Record.ExceptionAsync(() => node.ConsumeAsync(job, dryRun: false));

        string because = await DescribeAsync(source, thrown, destination, output);
        thrown.Should().BeAssignableTo<IOException>(because);
        new FileInfo(destination).Length.Should().Be(PreviousBytes.Length, because);
        (await File.ReadAllBytesAsync(destination)).Should().Equal(PreviousBytes, because);
        Directory.GetFiles(output, "*.tmp").Should().BeEmpty(because);
    }

    [Fact]
    public async Task Destination_file_link_is_refused()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        string outside = CreateFolder(dir, "outside");
        await File.WriteAllTextAsync(Path.Combine(input, "victim.txt"), "NEW");
        string target = Path.Combine(outside, "target.txt");
        await File.WriteAllTextAsync(target, "OUTSIDE");
        string link = Path.Combine(output, "victim.txt");
        LinkFactory.CreateFileSymlink(link, target);

        ExecutionResult result = await RunAsync(input, new { path = output, mode = "copy", overwrite = true });

        string outsideContent = await File.ReadAllTextAsync(target);
        string? linkTarget = new FileInfo(link).LinkTarget;
        FileJob? job = result.Jobs.Count == 1 ? result.Jobs[0] : null;
        string because = Report(
            $"jobs={result.Jobs.Count} status={job?.Status} error={job?.ErrorMessage}",
            $"outside/target.txt = {outsideContent}",
            $"out/victim.txt -> {linkTarget}");
        result.Jobs.Should().ContainSingle(because);
        job!.Status.Should().Be(FileJobStatus.Failed, because);
        job.ErrorMessage.Should().StartWith("Output 'FolderOutput' failed: FolderOutput: ", because);
        outsideContent.Should().Be("OUTSIDE", because);
        linkTarget.Should().Be(target, because);
    }

    [Fact]
    public async Task Move_deletes_only_the_delivered_path()
    {
        // In to out: the source goes once the copy is in place.
        using (var dir = new TempDirectory())
        {
            string input = CreateFolder(dir, "in");
            string output = CreateFolder(dir, "out");
            await File.WriteAllTextAsync(Path.Combine(input, "f.txt"), "MOVED");

            ExecutionResult result = await RunAsync(input, new { path = output, mode = "move" });

            string because = await DescribeFolderAsync("in to out", result, input, output);
            result.Succeeded.Should().Be(1, because);
            (await File.ReadAllTextAsync(Path.Combine(output, "f.txt"))).Should().Be("MOVED", because);
            File.Exists(Path.Combine(input, "f.txt")).Should().BeFalse(because);
        }

        // Input and output on one folder: the delivered file is the source, so it stays.
        using (var dir = new TempDirectory())
        {
            string data = CreateFolder(dir, "data");
            await File.WriteAllTextAsync(Path.Combine(data, "f.txt"), "IN PLACE");

            ExecutionResult result = await RunAsync(data, new { path = data, mode = "move", overwrite = true });

            string because = await DescribeFolderAsync("same folder", result, data);
            result.Succeeded.Should().Be(1, because);
            File.Exists(Path.Combine(data, "f.txt")).Should().BeTrue(because);
            (await File.ReadAllTextAsync(Path.Combine(data, "f.txt"))).Should().Be("IN PLACE", because);
        }

        // Input through a linked folder, output on its target: still one file, so it stays.
        using (var dir = new TempDirectory())
        {
            string data = CreateFolder(dir, "data");
            string alias = Path.Combine(dir.Path, "alias");
            await LinkFactory.CreateDirectoryLinkAsync(alias, data);
            await File.WriteAllTextAsync(Path.Combine(data, "f.txt"), "ALIASED");

            ExecutionResult result = await RunAsync(alias, new { path = data, mode = "move", overwrite = true });

            string because = await DescribeFolderAsync("linked folder", result, data);
            result.Succeeded.Should().Be(1, because);
            File.Exists(Path.Combine(data, "f.txt")).Should().BeTrue(because);
            (await File.ReadAllTextAsync(Path.Combine(data, "f.txt"))).Should().Be("ALIASED", because);
        }
    }

    [Fact]
    public async Task Backup_holds_the_previous_bytes_and_replaces_an_older_backup()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        string source = Path.Combine(input, "p.txt");
        await File.WriteAllTextAsync(source, "NEW");
        string destination = Path.Combine(output, "p.txt");
        await File.WriteAllBytesAsync(destination, PreviousBytes);
        string backup = destination + ".bak";
        await File.WriteAllTextAsync(backup, "OLDER");
        FolderOutputNode node = CreateNode(new { path = output, overwrite = true, enableBackup = true });
        var job = new FileJob { OriginalPath = source, CurrentPath = source };

        await node.ConsumeAsync(job, dryRun: false);

        string because = Report(
            $"out/p.txt = {await File.ReadAllTextAsync(destination)}",
            $"out/p.txt.bak = {await File.ReadAllTextAsync(backup)}",
            $"out entries: {string.Join(", ", Directory.GetFileSystemEntries(output).Select(Path.GetFileName))}");
        (await File.ReadAllTextAsync(backup)).Should().Be("PREVIOUS", because);
        (await File.ReadAllTextAsync(destination)).Should().Be("NEW", because);
        Directory.GetFiles(output, "*.tmp").Should().BeEmpty(because);
    }

    [Fact]
    public async Task Linked_destination_directory_is_refused()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        string outside = CreateFolder(dir, "outside");
        Directory.CreateDirectory(Path.Combine(input, "sub"));
        await File.WriteAllTextAsync(Path.Combine(input, "sub", "f.txt"), "SUB");
        await LinkFactory.CreateDirectoryLinkAsync(Path.Combine(output, "sub"), outside);

        ExecutionResult result = await RunAsync(
            new { path = input, recursive = true },
            new { path = output, preserveStructure = true, sourceBasePath = input });

        string[] outsideEntries = Directory.GetFileSystemEntries(outside);
        string because = Report(
            $"jobs={result.Jobs.Count} failed={result.Failed} errors={string.Join(" | ", result.Jobs.Select(j => j.ErrorMessage))}",
            $"outside entries={outsideEntries.Length}: {string.Join(", ", outsideEntries.Select(Path.GetFileName))}");
        result.Jobs.Should().ContainSingle(because).Which.Status.Should().Be(FileJobStatus.Failed, because);
        outsideEntries.Should().BeEmpty(because);
    }

    [Fact]
    public async Task File_outside_the_base_is_delivered_flat()
    {
        using var dir = new TempDirectory();
        string sourceBase = CreateFolder(dir, "in");
        string input = CreateFolder(dir, "input2");
        string output = CreateFolder(dir, "out");
        await File.WriteAllTextAsync(Path.Combine(input, "g.txt"), "FLAT");

        ExecutionResult result = await RunAsync(
            new { path = input },
            new { path = output, preserveStructure = true, sourceBasePath = sourceBase });

        string[] delivered = Directory.GetFiles(output, "*", SearchOption.AllDirectories);
        string because = Report(
            $"succeeded={result.Succeeded} failed={result.Failed} errors={string.Join(" | ", result.Jobs.Select(j => j.ErrorMessage))}",
            $"out files: {string.Join(", ", delivered.Select(f => Path.GetRelativePath(output, f)))}");
        result.Succeeded.Should().Be(1, because);
        File.Exists(Path.Combine(output, "g.txt")).Should().BeTrue(because);
    }

    private static async Task WriteZerosUntilCancelledAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        byte[] block = new byte[64 * 1024];
        try
        {
            await pipe.WaitForConnectionAsync(ct);
            while (!ct.IsCancellationRequested)
            {
                await pipe.WriteAsync(block, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // The reader cancelling closes the pipe; either way the writer is done.
        }
    }

    private static string CreateFolder(TempDirectory dir, string name)
    {
        string path = Path.Combine(dir.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static FolderOutputNode CreateNode(object config)
    {
        var node = new FolderOutputNode(NullLogger<FolderOutputNode>.Instance);
        node.Configure(Config(config));
        return node;
    }

    private static Dictionary<string, JsonElement> Config(object config) =>
        JsonSerializer.SerializeToElement(config).EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);

    private static Task<ExecutionResult> RunAsync(string input, object outputConfig) =>
        RunAsync(new { path = input }, outputConfig);

    private static Task<ExecutionResult> RunAsync(object inputConfig, object outputConfig)
    {
        var source = new NodeDefinition { TypeKey = "FolderInput", Config = Config(inputConfig) };
        var sink = new NodeDefinition { TypeKey = "FolderOutput", Config = Config(outputConfig) };
        var graph = new PipelineGraph
        {
            Nodes = { source, sink },
            Connections = { new Connection { FromNode = source.Id, ToNode = sink.Id } },
        };
        var runner = new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance);
        return runner.RunAsync(graph);
    }

    private async Task<string> DescribeAsync(string source, Exception? thrown, string destination, string output)
    {
        long length = new FileInfo(destination).Length;
        string content = length <= 64 ? Encoding.UTF8.GetString(await File.ReadAllBytesAsync(destination)) : "(not shown)";
        return Report(
            $"source={source} thrown={thrown?.GetType().Name}: {thrown?.Message}",
            $"destination length={length} content={content}",
            $"out entries: {string.Join(", ", Directory.GetFileSystemEntries(output).Select(Path.GetFileName))}");
    }

    private async Task<string> DescribeFolderAsync(string label, ExecutionResult result, params string[] folders)
    {
        var lines = new List<string> { $"{label}: succeeded={result.Succeeded} failed={result.Failed}" };
        lines.AddRange(result.Jobs.Select(j => $"job {j.Status}: {j.ErrorMessage}"));
        foreach (string folder in folders)
        {
            foreach (string file in Directory.GetFiles(folder))
            {
                lines.Add($"{Path.GetFileName(folder)}/{Path.GetFileName(file)} = {await File.ReadAllTextAsync(file)}");
            }
        }

        return Report([.. lines]);
    }

    private string Report(params string[] lines)
    {
        foreach (string line in lines)
        {
            _output.WriteLine(line);
        }

        return string.Join("; ", lines);
    }
}
