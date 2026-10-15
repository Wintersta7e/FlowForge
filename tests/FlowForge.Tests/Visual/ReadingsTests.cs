using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The numbers the console, toolbar, HUD and station gauges show: each one comes from the run,
/// and a slot nothing measures reads a dash.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ReadingsTests
{
    private const string NoReading = "—";

    private static readonly string[] GaugeIds = ["GaugeT", "GaugeP", "GaugeD"];

    private readonly ITestOutputHelper _output;

    public ReadingsTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Readings_FollowTheRun()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        ExecutionLogViewModel log = vm.ExecutionLog;
        string input = Path.Combine(session.Temp.Path, "in");
        TestFileFactory.CreateTestImages(input, "a.png", "b.png", "c.png");

        // An image extension over text bytes: the convert step cannot decode these, so each one fails.
        session.Temp.CreateFiles(Path.Combine("in", "bad1.png"), Path.Combine("in", "bad2.png"));
        const int files = 5;
        const int failing = 2;
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, filter = "*.png" })
            .AddTransform("ImageConvert", new { format = "jpg" })
            .AddOutput("FolderOutput", new { path = session.Temp.OutputPath, mode = "copy" })
            .Build();
        vm.Editor.LoadGraph(graph, vm.Registry).Should().Be(0, "the three stations connect in a chain");

        vm.RunCommand.Execute(null);
        session.RenderUntil(() => !log.IsRunning && log.Summary is not null, TimeSpan.FromSeconds(20), "real run finished");
        session.Pump();

        string source = Slot(session, "ToolbarSourceCount");
        string errors = Slot(session, "HudErrors");
        string count = Slot(session, "ConsoleCount");
        string latency = Slot(session, "HudLatency");
        string pid = Slot(session, "ConsolePid");
        _output.WriteLine($"[readings] files={files} failing={failing} summary='{log.Summary}'");
        _output.WriteLine($"[readings] src={source} err={errors} count={count} lat={latency} pid={pid}");
        source.Should().Be(files.ToString(CultureInfo.InvariantCulture), "the toolbar counts the files the source found");
        errors.Should().Be(failing.ToString(CultureInfo.InvariantCulture), "the HUD counts the files that failed");
        count.Should().Be($"{files}/{files}", "every original reached an end");
        latency.Should().MatchRegex(new Regex(@"^\d+\.\dms$", RegexOptions.None, TimeSpan.FromSeconds(1)), "latency is the measured mean per finished file");
        pid.Should().Be(NoReading, "no run is in progress");
    }

    [AvaloniaFact]
    public void IdleRunningAndDemoStates_ShowOnlySourcedReadings()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        int stations = vm.Editor.Nodes.Count;
        string runningPid = "0x" + Environment.ProcessId.ToString("x", CultureInfo.InvariantCulture);

        try
        {
            AssertUnsourcedReadings(session, "idle", stations);

            vm.ExecutionLog.IsRunning = true;
            session.Pump();
            AssertUnsourcedReadings(session, "running", stations);
            string pid = Slot(session, "ConsolePid");
            _output.WriteLine($"[running] pid={pid} expected={runningPid}");
            pid.Should().Be(runningPid, "the console names this process while a run is in progress");
            vm.ExecutionLog.IsRunning = false;

            vm.IsDemoMode = true;
            session.Pump();
            AssertUnsourcedReadings(session, "demo", stations);
        }
        finally
        {
            // The stations animate while either flag is on; stop them before the window closes.
            vm.IsDemoMode = false;
            vm.ExecutionLog.IsRunning = false;
        }
    }

    [AvaloniaFact]
    public void DemoConsole_ShowsDemoStateWithoutReadings()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        session.LoadTemplate(VisualSession.WebExportTemplate);

        vm.IsDemoMode = true;
        try
        {
            session.Pump();
            string pill = string.Join("|", session.ShownText(session.ById<StackPanel>("ConsoleState")));
            _output.WriteLine($"[demo] pill={pill}");
            pill.Should().Be("DEMO", "the console names the demo, not a cold or lit forge");

            TextBlock caption = session.ById<TextBlock>("ConsoleDemoCaption");
            TextBlock idleCaption = session.ById<TextBlock>("ConsoleIdleCaption");
            Dictionary<string, bool> readings = new[] { "ConsolePid", "ConsoleCount", "ConsoleThroughput" }
                .ToDictionary(id => id, id => session.ById<TextBlock>(id).IsEffectivelyVisible, StringComparer.Ordinal);
            _output.WriteLine($"[demo] caption='{caption.Text}' visible={caption.IsEffectivelyVisible} idle-caption visible={idleCaption.IsEffectivelyVisible}");
            _output.WriteLine("[demo] " + string.Join(" ", readings.Select(r => $"{r.Key} visible={r.Value}")));
            caption.IsEffectivelyVisible.Should().BeTrue();
            caption.Text.Should().Be("// demo — no files touched");
            idleCaption.IsEffectivelyVisible.Should().BeFalse("the idle caption gives way to the demo caption");
            readings.Should().OnlyContain(r => !r.Value, "a demo touches no files, so it has no process, count or throughput to show");
        }
        finally
        {
            // The beads animate for as long as DEMO mode is on; stop it before the window closes.
            vm.IsDemoMode = false;
        }
    }

    [AvaloniaFact]
    public void RunningConsole_HasExactlyOneProgressBar()
    {
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;

        log.IsRunning = true;
        try
        {
            session.Pump();
            ExecutionLogView view = session.Window.GetVisualDescendants().OfType<ExecutionLogView>().Single();
            int bars = view.GetVisualDescendants().OfType<ProgressBar>().Count(bar => bar.IsEffectivelyVisible);
            int phaseMembers = typeof(ExecutionLogViewModel)
                .GetMember("Phase", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Length;
            _output.WriteLine($"[running] visible progress bars={bars} Phase members={phaseMembers}");
            bars.Should().Be(1, "the console's bar is the run's only progress bar");
            phaseMembers.Should().Be(0, "the phase shows through the label, not a second state the view could bind");
        }
        finally
        {
            log.IsRunning = false;
        }
    }

    private static string Slot(VisualSession session, string automationId) =>
        session.ById<TextBlock>(automationId).Text ?? string.Empty;

    // Each station shows one value per gauge; its idle and running variants share the slot's id.
    private static List<string> ShownGauge(VisualSession session, string automationId) =>
        session.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && string.Equals(AutomationProperties.GetAutomationId(t), automationId, StringComparison.Ordinal))
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    private void AssertUnsourcedReadings(VisualSession session, string state, int stations)
    {
        string memory = Slot(session, "HudMemory");
        Dictionary<string, List<string>> gauges = GaugeIds.ToDictionary(id => id, id => ShownGauge(session, id), StringComparer.Ordinal);
        _output.WriteLine($"[{state}] mem={memory} " + string.Join(" ", gauges.Select(g => $"{g.Key}={string.Join(",", g.Value)}")));
        foreach ((string id, List<string> shown) in gauges)
        {
            shown.Should().HaveCount(stations, $"each station shows one {id} value when {state}");
            shown.Should().OnlyContain(value => string.Equals(value, NoReading, StringComparison.Ordinal), $"nothing measures {id} when {state}");
        }

        memory.Should().Be(NoReading, $"nothing measures memory when {state}");
    }
}
