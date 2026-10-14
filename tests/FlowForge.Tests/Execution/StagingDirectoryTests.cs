using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Execution;

public class StagingDirectoryTests
{
    private const UnixFileMode EveryoneMayWrite =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

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

    [Fact]
    public void Default_staging_root_is_the_users_data_folder_where_the_temp_folder_is_shared()
    {
        string expected = OperatingSystem.IsWindows()
            ? Path.Combine(Path.GetTempPath(), "FlowForge", "runs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FlowForge", "runs");

        _output.WriteLine($"[staging] default={PipelineRunner.DefaultStagingRoot}");
        PipelineRunner.DefaultStagingRoot.Should().Be(expected);
        if (!OperatingSystem.IsWindows())
        {
            PipelineRunner.DefaultStagingRoot.Should().NotStartWith(Path.GetTempPath());
        }
    }

    [Fact]
    public void Staging_without_a_per_user_folder_is_refused()
    {
        using var context = new RunContext(dryRun: false, stagingRoot: null);

        Action stage = () => context.NewStagingPath(".txt");

        stage.Should().Throw<IOException>().WithMessage("No per-user data folder*");
    }

    [Fact]
    public async Task Missing_staging_folders_are_created_private()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt");
        string stagingRoot = Path.Combine(dir.Path, "FlowForge", "runs");

        ExecutionResult result = await CreateRunner(stagingRoot, new StagingProbeNode()).RunAsync(ProbeGraph(dir));

        result.Succeeded.Should().Be(1);
        if (!OperatingSystem.IsWindows())
        {
            UnixFileMode parentMode = File.GetUnixFileMode(Path.GetDirectoryName(stagingRoot)!);
            UnixFileMode rootMode = File.GetUnixFileMode(stagingRoot);
            _output.WriteLine($"[staging] parent={parentMode} root={rootMode}");
            parentMode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            rootMode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task Staging_refuses_a_root_that_other_users_can_write()
    {
        using var dir = new TempDirectory();
        dir.CreateFiles("a.txt", "b.txt");
        string stale = Path.Combine(dir.StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stale);
        await File.WriteAllBytesAsync(Path.Combine(stale, ".lock"), []);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(dir.StagingRoot, EveryoneMayWrite);
        }

        var before = TreeSnapshot.Take(dir.Path, "output", ".staging");
        ExecutionResult result = await CreateRunner(dir.StagingRoot, new StagingProbeNode()).RunAsync(ProbeGraph(dir));

        string listing = string.Join(", ", Directory.GetFileSystemEntries(dir.StagingRoot).Select(Path.GetFileName));
        _output.WriteLine($"[staging] failed={result.Failed} entries={listing}");
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(dir.Path, "output", ".staging"), _output, "input");
        if (!OperatingSystem.IsWindows())
        {
            string refusal = $"The staging folder '{dir.StagingRoot}' is writable by other users";
            result.Failed.Should().Be(2, listing);
            result.Jobs.Select(j => j.ErrorMessage).Should().AllSatisfy(m => m.Should().StartWith(refusal), listing);
            Directory.GetFileSystemEntries(dir.StagingRoot).Should().Equal([stale], "nothing is staged and the sweep deletes nothing");
            File.Exists(Path.Combine(stale, ".lock")).Should().BeTrue();
            dir.OutputFiles.Should().BeEmpty();
        }
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
