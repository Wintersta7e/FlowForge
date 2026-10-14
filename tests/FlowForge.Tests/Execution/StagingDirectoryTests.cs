using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class StagingDirectoryTests
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;

    public StagingDirectoryTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(nameof(StagingProbeNode.Outcome.Succeed))]
    [InlineData(nameof(StagingProbeNode.Outcome.Fail))]
    [InlineData(nameof(StagingProbeNode.Outcome.Cancel))]
    public async Task Run_directory_is_removed_after_each_outcome(string behaviour)
    {
        StagingProbeNode.Outcome outcome = Enum.Parse<StagingProbeNode.Outcome>(behaviour);
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        using var cts = new CancellationTokenSource();
        var probe = new StagingProbeNode { Behaviour = outcome, Cancellation = cts };

        Func<Task> run = () => CreateRunner(dir.StagingRoot, probe).RunAsync(ProbeGraph(dir), ct: cts.Token);

        if (outcome == StagingProbeNode.Outcome.Cancel)
        {
            await run.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await run();
        }

        string[] entries = Directory.GetFileSystemEntries(dir.StagingRoot);
        _output.WriteLine($"[staging] entries={entries.Length}");
        entries.Should().BeEmpty();
        Directory.GetFiles(dir.OutputPath, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
        if (!OperatingSystem.IsWindows())
        {
            probe.ObservedRunDirectoryMode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Sweep_spares_live_runs_and_foreign_entries()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");
        var probe = new StagingProbeNode { Behaviour = StagingProbeNode.Outcome.Block };
        Task<ExecutionResult> runA = CreateRunner(dir.StagingRoot, probe).RunAsync(ProbeGraph(dir));
        string runDirectoryA = Path.GetDirectoryName(await probe.Staged.Task.WaitAsync(HangGuard))!;

        string stale = Path.Combine(dir.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        await File.WriteAllBytesAsync(Path.Combine(stale, ".lock"), []);
        string foreign = Path.Combine(dir.StagingRoot, "not-a-run");
        Directory.CreateDirectory(foreign);
        await File.WriteAllBytesAsync(Path.Combine(foreign, ".lock"), []);
        string unlocked = Path.Combine(dir.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(unlocked);
        string outside = Path.Combine(dir.Path, "outside");
        Directory.CreateDirectory(outside);
        string target = Path.Combine(outside, "target.txt");
        await File.WriteAllTextAsync(target, "TARGET");
        string link = Path.Combine(dir.StagingRoot, Guid.NewGuid().ToString("N"));
        LinkFactory.CreateDirectorySymlink(link, outside);
        string? linkTarget = new DirectoryInfo(link).LinkTarget;

        string inputB = Path.Combine(dir.Path, "b-in");
        Directory.CreateDirectory(inputB);
        await File.WriteAllTextAsync(Path.Combine(inputB, "b.txt"), "B");
        PipelineGraph graphB = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = inputB })
            .AddOutput("FolderOutput", new { path = Path.Combine(dir.Path, "b-out") })
            .Build();
        ExecutionResult resultB = await CreateRunner(dir.StagingRoot, new StagingProbeNode()).RunAsync(graphB);

        string listing = string.Join(", ", Directory.GetFileSystemEntries(dir.StagingRoot).Select(Path.GetFileName));
        _output.WriteLine($"[sweep] after run B: {listing}");
        resultB.Succeeded.Should().Be(1, listing);
        Directory.Exists(runDirectoryA).Should().BeTrue("a live run's folder is locked: {0}", listing);
        Directory.Exists(stale).Should().BeFalse("a stale run's folder is swept: {0}", listing);
        Directory.Exists(foreign).Should().BeTrue("a folder not named like a run is not touched: {0}", listing);
        Directory.Exists(unlocked).Should().BeTrue("a folder without a lock file is not touched: {0}", listing);
        new DirectoryInfo(link).LinkTarget.Should().Be(linkTarget, listing);
        (await File.ReadAllTextAsync(target)).Should().Be("TARGET");

        probe.Release.SetResult();
        ExecutionResult resultA = await runA.WaitAsync(HangGuard);
        resultA.Succeeded.Should().Be(1);
        Directory.Exists(runDirectoryA).Should().BeFalse();
    }

    private static PipelineGraph ProbeGraph(TempDirectory dir) =>
        PipelineBuilder.New()
            .AddSource("FolderInput", new { path = dir.Path, recursive = false })
            .AddTransform(StagingProbeNode.RegisteredTypeKey, new { })
            .AddOutput("FolderOutput", new { path = dir.OutputPath })
            .Build();

    private static PipelineRunner CreateRunner(string stagingRoot, StagingProbeNode probe)
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        registry.Register<StagingProbeNode>(
            StagingProbeNode.RegisteredTypeKey,
            () => probe,
            "Staging Probe",
            NodeCategory.Transform,
            () => []);
        return new PipelineRunner(registry, NullLogger<PipelineRunner>.Instance, maxConcurrency: 2, stagingRoot: stagingRoot);
    }
}
