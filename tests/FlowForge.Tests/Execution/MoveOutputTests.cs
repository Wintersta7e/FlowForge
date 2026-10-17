using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class MoveOutputTests
{
    private readonly ITestOutputHelper _output;

    public MoveOutputTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task In_place_move_keeps_the_delivered_file()
    {
        using var dir = new TempDirectory();
        string data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        string file = Path.Combine(data, "f.txt");
        await File.WriteAllTextAsync(file, "IN PLACE");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = data })
            .AddTransform("RenamePattern", new { pattern = "{name}{ext}" })
            .AddOutput("FolderOutput", new { path = data, mode = "move", overwrite = true })
            .Build();

        ExecutionResult result = await new PipelineRunner(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, stagingRoot: dir.StagingRoot).RunAsync(graph);

        string listing = $"succeeded={result.Succeeded} failed={result.Failed} data: "
            + string.Join(", ", Directory.GetFiles(data).Select(f => Path.GetFileName(f)));
        _output.WriteLine($"[move] {listing}");
        result.Succeeded.Should().Be(1, listing);
        File.Exists(file).Should().BeTrue(listing);
        (await File.ReadAllTextAsync(file)).Should().Be("IN PLACE", listing);
    }

    [Fact]
    public async Task Copy_and_move_outputs_both_deliver_before_the_original_is_deleted()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        string original = Path.Combine(input, "f.txt");
        await File.WriteAllTextAsync(original, "ORIGINAL");
        string copyOutput = Path.Combine(dir.Path, "outA");
        string moveOutput = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input });
        NodeDefinition copy = PipelineBuilder.Node("FolderOutput", new { path = copyOutput, mode = "copy" });
        NodeDefinition move = PipelineBuilder.Node("FolderOutput", new { path = moveOutput, mode = "move" });
        var graph = new PipelineGraph
        {
            Nodes = { source, copy, move },
            Connections = { Wire(source, copy), Wire(source, move) },
        };

        ExecutionResult result = await CreateRunner(dir).RunAsync(graph);

        string listing = Listing(result, input, copyOutput, moveOutput);
        _output.WriteLine($"[copy+move] {listing}");
        result.Succeeded.Should().Be(2, listing);
        (await File.ReadAllTextAsync(Path.Combine(copyOutput, "f.txt"))).Should().Be("ORIGINAL", listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "f.txt"))).Should().Be("ORIGINAL", listing);
        File.Exists(original).Should().BeFalse(listing);
    }

    [Fact]
    public async Task Failing_branch_keeps_the_original()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        string original = Path.Combine(input, "f.jpg");
        await File.WriteAllTextAsync(original, "NOT AN IMAGE");
        string moveOutput = Path.Combine(dir.Path, "outA");
        string convertOutput = Path.Combine(dir.Path, "outB");
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input });
        NodeDefinition move = PipelineBuilder.Node("FolderOutput", new { path = moveOutput, mode = "move" });
        NodeDefinition convert = PipelineBuilder.Node("ImageConvert", new { format = "png" });
        NodeDefinition converted = PipelineBuilder.Node("FolderOutput", new { path = convertOutput, mode = "copy" });
        var graph = new PipelineGraph
        {
            Nodes = { source, move, convert, converted },
            Connections = { Wire(source, move), Wire(source, convert), Wire(convert, converted) },
        };

        ExecutionResult result = await CreateRunner(dir).RunAsync(graph);

        string listing = Listing(result, input, moveOutput, convertOutput);
        _output.WriteLine($"[failing-branch] {listing}");
        result.Succeeded.Should().Be(1, listing);
        result.Failed.Should().Be(1, listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "f.jpg"))).Should().Be("NOT AN IMAGE", listing);
        File.Exists(original).Should().BeTrue(listing);
        (await File.ReadAllTextAsync(original)).Should().Be("NOT AN IMAGE", listing);
    }

    [Fact]
    public async Task Extra_job_is_delivered_before_the_moved_original_is_deleted()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        Directory.CreateDirectory(input);
        string original = Path.Combine(input, "f.txt");
        await File.WriteAllTextAsync(original, "ORIGINAL");
        string moveOutput = Path.Combine(dir.Path, "out");
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register(ExtraCopyTransform.RegisteredTypeKey, () => new ExtraCopyTransform(), "Extra Copy", NodeCategory.Transform, () => []);
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input })
            .AddTransform(ExtraCopyTransform.RegisteredTypeKey, new { })
            .AddOutput("FolderOutput", new { path = moveOutput, mode = "move" })
            .Build();

        // One delivery at a time, so a deletion after the first delivery would come before the second.
        ExecutionResult result = await new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 1, stagingRoot: dir.StagingRoot)
            .RunAsync(graph);

        string listing = Listing(result, input, moveOutput);
        _output.WriteLine($"[extra-job] {listing}");
        result.Succeeded.Should().Be(2, listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "f.txt"))).Should().Be("ORIGINAL", listing);
        (await File.ReadAllTextAsync(Path.Combine(moveOutput, "copy-f.txt"))).Should().Be("ORIGINAL", listing);
        File.Exists(original).Should().BeFalse(listing);
    }

    [Fact]
    public async Task In_place_move_never_deletes_another_files_delivery()
    {
        using var dir = new TempDirectory();
        string data = await CreateRenumberCaseAsync(dir);

        ExecutionResult result = await CreateRunner(dir, maxConcurrency: 2)
            .RunAsync(RenumberInPlace(data, new { path = data, mode = "move", overwrite = true }));

        string listing = await ContentsAsync(result, data);
        _output.WriteLine($"[renumber-move] {listing}");
        result.Succeeded.Should().Be(2, listing);
        result.Failed.Should().Be(0, listing);
        Directory.GetFiles(data).Select(f => Path.GetFileName(f)).Should().BeEquivalentTo(["001.txt", "002.txt"], listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "001.txt"))).Should().Be("TWO", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "002.txt"))).Should().Be("AAA", listing);
    }

    [Fact]
    public async Task In_place_move_that_cannot_copy_aside_fails_the_overwriting_file()
    {
        using var dir = new TempDirectory();
        string data = await CreateRenumberCaseAsync(dir);
        string blocker = Path.Combine(dir.Path, "blocker");
        await File.WriteAllTextAsync(blocker, "a file where the staging folder would go");
        var runner = new PipelineRunner(
            NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, maxConcurrency: 2, Path.Combine(blocker, "staging"));

        ExecutionResult result = await runner.RunAsync(RenumberInPlace(data, new { path = data, mode = "move", overwrite = true }));

        string listing = await ContentsAsync(result, data);
        _output.WriteLine($"[renumber-unstaged] {listing}");
        result.Succeeded.Should().Be(1, listing);
        FileJob failed = result.Jobs.Should().ContainSingle(j => j.Status == FileJobStatus.Failed, listing).Subject;
        failed.OriginalPath.Should().Be(Path.Combine(data, "a.txt"), listing);
        failed.ErrorMessage.Should().Contain($"'{Path.Combine(data, "002.txt")}'", listing).And.Contain($"'{Path.Combine(data, "a.txt")}'", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "001.txt"))).Should().Be("TWO", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "002.txt"))).Should().Be("TWO", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "a.txt"))).Should().Be("AAA", listing);
    }

    [Fact]
    public async Task Dry_run_of_an_in_place_renumber_names_the_input_it_copies_aside()
    {
        using var dir = new TempDirectory();
        string data = await CreateRenumberCaseAsync(dir);
        var before = TreeSnapshot.Take(data);

        ExecutionResult result = await CreateRunner(dir, maxConcurrency: 2)
            .RunAsync(RenumberInPlace(data, new { path = data, mode = "move", overwrite = true }), dryRun: true);

        string listing = string.Join(" | ", result.Jobs.Select(j => $"{Path.GetFileName(j.OriginalPath)}→{j.FileName} {j.Status}: {string.Join("; ", j.NodeLog)}"));
        _output.WriteLine($"[renumber-dry] {listing}");
        result.Succeeded.Should().Be(2, listing);
        result.Jobs.Select(j => j.FileName).Should().BeEquivalentTo(["001.txt", "002.txt"], listing);
        string copyAside = $"Would copy '{Path.Combine(data, "002.txt")}' aside first: another delivery of this run overwrites it [dry-run]";
        FileJob reader = result.Jobs.Single(j => string.Equals(j.OriginalPath, Path.Combine(data, "002.txt"), StringComparison.Ordinal));
        FileJob overwriter = result.Jobs.Single(j => string.Equals(j.OriginalPath, Path.Combine(data, "a.txt"), StringComparison.Ordinal));
        reader.NodeLog.Should().ContainSingle(line => string.Equals(line, copyAside, StringComparison.Ordinal), listing);
        overwriter.NodeLog.Should().NotContain(line => line.Contains("aside", StringComparison.Ordinal), listing);
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(data), _output, "renumber dry run");
        Directory.Exists(dir.StagingRoot).Should().BeFalse(listing);
    }

    [Fact]
    public async Task In_place_move_with_backup_never_deletes_another_files_delivery()
    {
        using var dir = new TempDirectory();
        string data = await CreateRenumberCaseAsync(dir);

        ExecutionResult result = await CreateRunner(dir, maxConcurrency: 2)
            .RunAsync(RenumberInPlace(data, new { path = data, mode = "move", overwrite = true, enableBackup = true }));

        string listing = await ContentsAsync(result, data);
        _output.WriteLine($"[renumber-backup] {listing}");
        result.Succeeded.Should().Be(2, listing);
        result.Failed.Should().Be(0, listing);
        Directory.GetFiles(data).Select(f => Path.GetFileName(f)).Should().BeEquivalentTo(["001.txt", "002.txt", "002.txt.bak"], listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "001.txt"))).Should().Be("TWO", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "002.txt"))).Should().Be("AAA", listing);
        (await File.ReadAllTextAsync(Path.Combine(data, "002.txt.bak"))).Should().Be("TWO", listing);
    }

    [Fact]
    public async Task Second_run_into_a_subfolder_of_a_recursive_source_keeps_both()
    {
        using var dir = new TempDirectory();
        string input = Path.Combine(dir.Path, "in");
        string output = Path.Combine(input, "out");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(input, "a.txt"), "NEW");
        await File.WriteAllTextAsync(Path.Combine(output, "a.txt"), "OLD");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, recursive = true })
            .AddOutput("FolderOutput", new { path = output, mode = "move", overwrite = true })
            .Build();

        // One delivery at a time: the file already in the output is read only after the new one replaced it.
        ExecutionResult result = await CreateRunner(dir, maxConcurrency: 1).RunAsync(graph);

        string listing = await ContentsAsync(result, input, output);
        _output.WriteLine($"[recursive-move] {listing}");
        result.Succeeded.Should().Be(2, listing);
        File.Exists(Path.Combine(input, "a.txt")).Should().BeFalse(listing);
        string[] delivered = await Task.WhenAll(Directory.GetFiles(output).Select(f => File.ReadAllTextAsync(f)));
        delivered.Should().BeEquivalentTo(["NEW", "OLD"], listing);
    }

    /// <summary>A folder renumbered in place: <c>002.txt</c> becomes <c>001.txt</c> and <c>a.txt</c> becomes <c>002.txt</c>.</summary>
    private static async Task<string> CreateRenumberCaseAsync(TempDirectory dir)
    {
        string data = Path.Combine(dir.Path, "data");
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(Path.Combine(data, "002.txt"), "TWO");
        await File.WriteAllTextAsync(Path.Combine(data, "a.txt"), "AAA");
        return data;
    }

    private static PipelineGraph RenumberInPlace(string data, object outputConfig) =>
        PipelineBuilder.New()
            .AddSource("FolderInput", new { path = data, filter = "*.txt" })
            .AddTransform("Sort", new { field = "filename", direction = "asc" })
            .AddTransform("RenamePattern", new { pattern = "{counter:000}{ext}", startIndex = 1 })
            .AddOutput("FolderOutput", outputConfig)
            .Build();

    private static async Task<string> ContentsAsync(ExecutionResult result, params string[] folders)
    {
        var parts = new List<string> { $"succeeded={result.Succeeded} failed={result.Failed} errors=[{string.Join("; ", result.Jobs.Select(j => j.ErrorMessage).OfType<string>())}]" };
        foreach (string folder in folders)
        {
            IEnumerable<string> files = Directory.Exists(folder) ? Directory.GetFiles(folder).Order(StringComparer.Ordinal) : [];
            foreach (string file in files)
            {
                parts.Add($"{Path.GetFileName(folder)}/{Path.GetFileName(file)}={await File.ReadAllTextAsync(file)}");
            }
        }

        return string.Join(" ", parts);
    }

    private static PipelineRunner CreateRunner(TempDirectory dir, int maxConcurrency = 0) =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, maxConcurrency, dir.StagingRoot);

    private static Connection Wire(NodeDefinition from, NodeDefinition to) => new() { FromNode = from.Id, ToNode = to.Id };

    private static string Listing(ExecutionResult result, params string[] folders) =>
        $"succeeded={result.Succeeded} failed={result.Failed} errors=[{string.Join("; ", result.Jobs.Select(j => j.ErrorMessage).OfType<string>())}] "
        + string.Join(" ", folders.Select(folder => Path.GetFileName(folder) + ": "
            + (Directory.Exists(folder) ? string.Join(", ", Directory.GetFiles(folder).Select(f => Path.GetFileName(f))) : "-")));

    /// <summary>Returns each job together with one extra job that delivers the same content as copy-&lt;name&gt;.</summary>
    private sealed class ExtraCopyTransform : ITransformNode
    {
        public const string RegisteredTypeKey = "ExtraCopy";

        public string TypeKey => RegisteredTypeKey;

        public void Configure(IDictionary<string, JsonElement> config)
        {
        }

        public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
        {
            var extra = new FileJob
            {
                OriginalPath = job.OriginalPath,
                ContentPath = job.ContentPath,
                CurrentPath = Path.Combine(job.DirectoryName, "copy-" + job.FileName),
            };
            return Task.FromResult<IEnumerable<FileJob>>([job, extra]);
        }
    }
}
