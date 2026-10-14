using System.Security.Cryptography;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class DestinationCollisionTests
{
    private readonly ITestOutputHelper _output;

    public DestinationCollisionTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Existing_destination_without_overwrite_fails()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        await File.WriteAllTextAsync(Path.Combine(input, "x.jpg"), "NEW");
        string existing = Path.Combine(output, "x.jpg");
        await File.WriteAllTextAsync(existing, "PREVIOUS");
        string hashBefore = Sha256(existing);

        ExecutionResult result = await CreateRunner().RunAsync(Graph(input, new { path = output, overwrite = false }));

        string because = Describe(result, output);
        result.Jobs.Should().ContainSingle(because);
        FileJob job = result.Jobs[0];
        job.Status.Should().Be(FileJobStatus.Failed, because);
        job.ErrorMessage.Should().StartWith("Output 'FolderOutput' failed: FolderOutput: ", because);
        Sha256(existing).Should().Be(hashBefore, because);
    }

    [Fact]
    public async Task Collision_suffixes_are_identical_at_any_concurrency()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        foreach (string folder in new[] { "s1", "s2", "s3", "s4", "s5" })
        {
            Directory.CreateDirectory(Path.Combine(input, folder));
            foreach (string name in new[] { "a.txt", "b.txt", "c.txt", "d.txt" })
            {
                await File.WriteAllTextAsync(Path.Combine(input, folder, name), $"{folder}/{name}");
            }
        }

        var maps = new List<(string Label, IReadOnlyDictionary<string, string> Map)>();
        int[] concurrencies = [1, 1, 1, 1, 1, .. Enumerable.Repeat(Environment.ProcessorCount, 5)];
        for (int run = 0; run < concurrencies.Length; run++)
        {
            string output = CreateFolder(dir, $"out{run}");
            ExecutionResult result = await CreateRunner(concurrencies[run])
                .RunAsync(Graph(input, new { path = output, preserveStructure = false }, recursive: true));
            result.Failed.Should().Be(0, Describe(result, output));
            maps.Add(($"run {run} at {concurrencies[run]}", HashByName(output)));
        }

        IReadOnlyDictionary<string, string> first = maps[0].Map;
        _output.WriteLine($"[claims] names={first.Count}");
        first.Should().HaveCount(20);
        foreach ((string label, IReadOnlyDictionary<string, string> map) in maps.Skip(1))
        {
            map.Should().Equal(first, label);
        }
    }

    [Fact]
    public async Task Same_name_jobs_with_overwrite_on_never_replace_each_other()
    {
        using var dir = new TempDirectory();
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        Directory.CreateDirectory(Path.Combine(input, "s1"));
        Directory.CreateDirectory(Path.Combine(input, "s2"));
        await File.WriteAllTextAsync(Path.Combine(input, "s1", "x.txt"), "FIRST");
        await File.WriteAllTextAsync(Path.Combine(input, "s2", "x.txt"), "SECOND");
        await File.WriteAllTextAsync(Path.Combine(output, "x.txt"), "PREVIOUS");

        ExecutionResult result = await CreateRunner()
            .RunAsync(Graph(input, new { path = output, overwrite = true }, recursive: true));

        string because = Describe(result, output);
        result.Succeeded.Should().Be(2, because);
        Directory.GetFiles(output).Should().HaveCount(2, because);
        (await File.ReadAllTextAsync(Path.Combine(output, "x.txt"))).Should().Be("FIRST", because);
        (await File.ReadAllTextAsync(Path.Combine(output, "x_1.txt"))).Should().Be("SECOND", because);
    }

    [Fact]
    public async Task Backup_path_claimed_by_this_run_is_refused()
    {
        // Input order: p.jpg claims out/p.jpg and its backup out/p.jpg.bak, so the input p.jpg.bak gets a suffix.
        using (var dir = new TempDirectory())
        {
            (string input, string output) = await CreateBackupCaseAsync(dir);
            string oldHash = Sha256(Path.Combine(output, "p.jpg"));
            string pHash = Sha256(Path.Combine(input, "p.jpg"));
            string bakHash = Sha256(Path.Combine(input, "p.jpg.bak"));

            ExecutionResult result = await CreateRunner().RunAsync(
                Graph(input, new { path = output, overwrite = true, enableBackup = true }));

            IReadOnlyDictionary<string, string> delivered = HashByName(output);
            string because = Describe(result, output);
            _output.WriteLine($"[claims] files={delivered.Count}");
            result.Succeeded.Should().Be(2, because);
            delivered.Should().Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["p.jpg"] = pHash,
                    ["p.jpg.bak"] = oldHash,
                    ["p.jpg_1.bak"] = bakHash,
                },
                because);
        }

        // Name descending: p.jpg.bak claims out/p.jpg.bak first, so p.jpg cannot back up there and is refused.
        using (var dir = new TempDirectory())
        {
            (string input, string output) = await CreateBackupCaseAsync(dir);
            string oldHash = Sha256(Path.Combine(output, "p.jpg"));
            string pHash = Sha256(Path.Combine(input, "p.jpg"));
            string bakHash = Sha256(Path.Combine(input, "p.jpg.bak"));
            PipelineGraph graph = PipelineBuilder.New()
                .AddSource("FolderInput", new { path = input })
                .AddTransform("Sort", new { field = "filename", direction = "desc" })
                .AddOutput("FolderOutput", new { path = output, overwrite = true, enableBackup = true })
                .Build();

            ExecutionResult result = await CreateRunner().RunAsync(graph);

            IReadOnlyDictionary<string, string> delivered = HashByName(output);
            string because = Describe(result, output);
            _output.WriteLine($"[claims] files={delivered.Count}");
            result.Succeeded.Should().Be(1, because);
            result.Jobs.Should().ContainSingle(j => j.Status == FileJobStatus.Failed, because)
                .Which.OriginalPath.Should().Be(Path.Combine(input, "p.jpg"), because);
            delivered.Should().Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["p.jpg"] = oldHash,
                    ["p.jpg.bak"] = bakHash,
                },
                because);
            Sha256(Path.Combine(input, "p.jpg")).Should().Be(pHash, because);
        }
    }

    private static async Task<(string Input, string Output)> CreateBackupCaseAsync(TempDirectory dir)
    {
        string input = CreateFolder(dir, "in");
        string output = CreateFolder(dir, "out");
        await File.WriteAllTextAsync(Path.Combine(input, "p.jpg"), "INPUT P");
        await File.WriteAllTextAsync(Path.Combine(input, "p.jpg.bak"), "INPUT P BAK");
        await File.WriteAllTextAsync(Path.Combine(output, "p.jpg"), "OLD OUTPUT");
        return (input, output);
    }

    private static PipelineGraph Graph(string input, object outputConfig, bool recursive = false) =>
        PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, recursive })
            .AddOutput("FolderOutput", outputConfig)
            .Build();

    private static PipelineRunner CreateRunner(int maxConcurrency = 2) =>
        new(NodeRegistry.CreateDefault(NullLoggerFactory.Instance), NullLogger<PipelineRunner>.Instance, maxConcurrency);

    private static string CreateFolder(TempDirectory dir, string name)
    {
        string path = Path.Combine(dir.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static IReadOnlyDictionary<string, string> HashByName(string folder) =>
        Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f), Sha256, StringComparer.Ordinal);

    private static string Describe(ExecutionResult result, string output)
    {
        var lines = new List<string> { $"succeeded={result.Succeeded} failed={result.Failed} skipped={result.Skipped}" };
        lines.AddRange(result.Jobs.Select(j => $"job {Path.GetFileName(j.OriginalPath)} {j.Status}: {j.ErrorMessage}"));
        lines.Add($"out: {string.Join(", ", Directory.GetFiles(output).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal))}");
        return string.Join(Environment.NewLine, lines);
    }
}
