using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;
using Ellipse = Avalonia.Controls.Shapes.Ellipse;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The execution log as a run fills it: counts, progress and the current file on its strip,
/// one row per processed file with a failed row's error, and a real run started from the window.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ExecutionLogVisualTests
{
    // Per channel. The row background beside the marker must fall outside it, or the band proves nothing.
    private const int ColourTolerance = 16;

    private readonly ITestOutputHelper _output;

    public ExecutionLogVisualTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Progress_events_update_counts_bar_and_current_file()
    {
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        FileJob[] jobs =
        [
            Job(session, "first.png", FileJobStatus.Succeeded),
            Job(session, "second.png", FileJobStatus.Succeeded),
            Job(session, "third.png", FileJobStatus.Failed, "Unreadable image header"),
        ];

        // The strip's bar and current file show only while a run is in progress.
        log.IsRunning = true;
        try
        {
            log.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
            log.ReportProgressEvent(new FilesDiscovered(4));
            log.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));

            // A branch of the second file ends first; the file counts toward progress only when its last branch does.
            log.ReportProgressEvent(new FileProcessed(Job(session, "second.png", FileJobStatus.Succeeded)) { CompletesOriginal = false });
            foreach (FileJob job in jobs)
            {
                log.ReportProgressEvent(new FileProcessed(job));
            }

            ExecutionLogView view = LogView(session.Window);
            TextBlock succeeded = session.ById<TextBlock>("SucceededCount");
            TextBlock failed = session.ById<TextBlock>("FailedCount");
            TextBlock phase = session.ById<TextBlock>("PhaseLabel");
            TextBlock count = session.ById<TextBlock>("ConsoleCount");
            session.Pump();
            ProgressBar bar = view.GetVisualDescendants().OfType<ProgressBar>().Single();
            session.RenderUntil(
                () => new Control[] { succeeded, failed, bar, phase }.All(c => c.IsEffectivelyVisible && c.Bounds.Width > 0),
                "log strip shows the fed counts");
            using WriteableBitmap frame = session.Capture(VisualFrames.LogProgress);

            _output.WriteLine($"succeeded={succeeded.Text} failed={failed.Text} count={count.Text} progress={bar.Value} phase='{phase.Text}'");
            succeeded.Text.Should().Be("3");
            failed.Text.Should().Be("1");
            count.Text.Should().Be("3/4", "three of four discovered files are finished");
            bar.Value.Should().BeApproximately(75, 0.001, "three of four discovered files are finished");
            phase.Text.Should().Be($"Processing 3 of 4 files — {jobs[^1].FileName}", "the strip counts finished files and names the last one");
        }
        finally
        {
            log.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Complete));
            log.IsRunning = false;
        }
    }

    [AvaloniaFact]
    public void Processed_jobs_render_rows_with_error_marker()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        FileJob failedJob = Job(session, "broken.png", FileJobStatus.Failed, "Unsupported image header");
        foreach (FileJob job in new[] { Job(session, "kept.png", FileJobStatus.Succeeded), failedJob, Job(session, "passed.png", FileJobStatus.Skipped) })
        {
            log.ReportProgressEvent(new FileProcessed(job));
        }

        ExecutionLogView view = LogView(window);
        ItemsControl rows = RowList(view, log);
        ToggleButton outputTab = Tab(view, "0");
        session.RenderUntil(() => RowsShown(rows, 1), "the succeeded file's row shown");

        // Pressing the open tab collapses the row list; pressing it again expands it.
        session.Click(outputTab);
        session.RenderUntil(() => !log.IsPanelExpanded && !rows.IsEffectivelyVisible, "row list collapsed");
        session.Click(outputTab);
        session.RenderUntil(() => log.IsPanelExpanded && RowsShown(rows, 1), "row list expanded");
        string[] outputFiles = RowGrids(rows).Select(g => ((FileJobLogEntryViewModel)g.DataContext!).FileName).ToArray();

        session.Click(Tab(view, "1"));
        session.RenderUntil(() => ListOf(view, log.ErrorEntries) is { } shown && RowsShown(shown, 1), "the failed file's row shown on the Errors tab");
        ItemsControl errorRows = ListOf(view, log.ErrorEntries)!;
        using WriteableBitmap frame = session.Capture(VisualFrames.LogRows);

        Grid failedRow = RowGrids(errorRows).Single();
        TextBlock error = failedRow.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 4);
        Ellipse marker = failedRow.Children.OfType<Ellipse>().Single(e => e.IsEffectivelyVisible);
        PixelRect markerBounds = session.BoundsInWindow(marker);
        var markerBand = new PixelRect(markerBounds.X + (markerBounds.Width / 2) - 1, markerBounds.Y + (markerBounds.Height / 2) - 1, 2, 2);

        // In the gap between the timestamp and the marker: background only, no text.
        var backgroundBand = new PixelRect(markerBounds.X - 6, markerBand.Y, 2, 2);
        Color measured = session.MeanColour(frame, markerBand);
        Color background = session.MeanColour(frame, backgroundBand);
        Color expected = ThemeColour("ForgeError");
        _output.WriteLine($"output rows={string.Join(",", outputFiles)} error rows={RowGrids(errorRows).Count} error='{error.Text}' visible={error.IsEffectivelyVisible}");
        _output.WriteLine($"marker band={markerBand} measured={measured} expected={expected} background={background} tolerance={ColourTolerance}");
        outputFiles.Should().Equal(["kept.png"], "the Output tab lists only the file that succeeded");
        error.IsEffectivelyVisible.Should().BeTrue();
        error.Text.Should().Be(failedJob.ErrorMessage);
        WithinTolerance(measured, expected).Should().BeTrue("the failed row's marker is drawn in the theme's error colour");
        WithinTolerance(background, expected).Should().BeFalse("the row background beside the marker is not the error colour");
    }

    [AvaloniaFact]
    public void Running_a_linear_pipeline_fills_the_log()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        ExecutionLogViewModel log = vm.ExecutionLog;
        string input = Path.Combine(session.Temp.Path, "in");
        TestFileFactory.CreateTestImages(input, "a.png", "b.png", "c.png");
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, filter = "*.png" })
            .AddOutput("FolderOutput", new { path = session.Temp.OutputPath, mode = "copy" })
            .Build();
        vm.Editor.LoadGraph(graph, vm.Registry).Should().Be(0, "the source connects to the output");

        vm.RunCommand.Execute(null);
        session.RenderUntil(() => !log.IsRunning, TimeSpan.FromSeconds(10), "real run finished");
        ExecutionLogView view = LogView(session.Window);
        ItemsControl rows = RowList(view, log);
        TextBlock summary = session.ById<TextBlock>("RunSummary");
        session.RenderUntil(() => RowsShown(rows, 3) && summary.IsEffectivelyVisible && summary.Bounds.Width > 0, "run rows and summary shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.RealRun);

        _output.WriteLine($"succeeded={log.Succeeded} failed={log.Failed} rows={RowGrids(rows).Count} summary='{summary.Text}'");
        log.Succeeded.Should().Be(3);
        log.Failed.Should().Be(0);
        RowGrids(rows).Should().HaveCount(3, "each copied file adds a row");
        log.Summary.Should().NotBeNullOrEmpty();
        summary.Text.Should().Be(log.Summary);
    }

    [AvaloniaFact]
    public void A_refused_run_keeps_its_summary_in_the_window_and_lists_it_as_errors()
    {
        const string prefix = "Execution failed: ";
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        ExecutionLogViewModel log = vm.ExecutionLog;
        string input = Path.Combine(session.Temp.Path, "in");
        TestFileFactory.CreateTestImages(input, "a.png");

        // The editor lets one step feed two outputs; the run refuses that shape with a long, node-by-node message.
        NodeDefinition source = PipelineBuilder.Node("FolderInput", new { path = input, filter = "*.png" });
        NodeDefinition rename = PipelineBuilder.Node("RenameAddAffix", new { prefix = "x_" });
        NodeDefinition first = PipelineBuilder.Node("FolderOutput", new { path = Path.Combine(session.Temp.OutputPath, "first"), mode = "copy" });
        NodeDefinition second = PipelineBuilder.Node("FolderOutput", new { path = Path.Combine(session.Temp.OutputPath, "second"), mode = "copy" });
        var graph = new PipelineGraph { Name = "fan-out", Nodes = [source, rename, first, second] };
        graph.Connections.Add(new Connection { FromNode = source.Id, ToNode = rename.Id });
        graph.Connections.Add(new Connection { FromNode = rename.Id, ToNode = first.Id });
        graph.Connections.Add(new Connection { FromNode = rename.Id, ToNode = second.Id });
        vm.Editor.LoadGraph(graph, vm.Registry).Should().Be(0, "the editor keeps every connection of the fan-out");

        vm.RunCommand.Execute(null);
        session.RenderUntil(() => !log.IsRunning && log.Summary is not null, TimeSpan.FromSeconds(10), "the run was refused");
        TextBlock summary = session.ById<TextBlock>("RunSummary");
        session.RenderUntil(() => summary.IsEffectivelyVisible && summary.Bounds.Width > 0, "refusal summary shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.RunRefused);

        PixelRect bounds = session.BoundsInWindow(summary);
        var client = PixelSize.FromSize(session.Window.ClientSize, session.Window.RenderScaling);
        object? tip = ToolTip.GetTip(summary);
        string[] lines = log.Summary![prefix.Length..].Split(Environment.NewLine);
        _output.WriteLine($"summary length={log.Summary.Length} bounds={bounds} client={client} tip length={tip?.ToString()?.Length ?? 0}");
        _output.WriteLine($"message lines={lines.Length} error rows={log.ErrorEntries.Count} failed={log.Failed}");
        log.Summary.Should().StartWith(prefix);
        tip.Should().Be(log.Summary, "the tooltip holds the whole summary");
        bounds.X.Should().BeGreaterThanOrEqualTo(0);
        bounds.Right.Should().BeLessThanOrEqualTo(client.Width, "the summary is trimmed inside the window, not cut off at its edge");
        log.ErrorEntries.Select(e => e.ErrorMessage).Should().Equal(lines, "each line of the message is a row on the Errors tab");
        log.Failed.Should().Be(0, "no file failed; the run was refused before it touched one");
    }

    [AvaloniaFact]
    public void LogRows_ShowTimestampAndNodeLogTooltip()
    {
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        foreach ((string name, FileJobStatus status) in new[] { ("kept.png", FileJobStatus.Succeeded), ("broken.png", FileJobStatus.Failed), ("passed.png", FileJobStatus.Skipped) })
        {
            FileJob job = Job(session, name, status, status == FileJobStatus.Failed ? "Unsupported image header" : null);
            job.NodeLog.Add($"ImageResize: read '{name}'");
            job.NodeLog.Add($"FolderOutput: finished '{name}'");
            log.ReportProgressEvent(new FileProcessed(job));
        }

        ExecutionLogView view = LogView(session.Window);
        var tabs = new (int Index, IReadOnlyList<FileJobLogEntryViewModel> Entries)[] { (0, log.Entries), (1, log.ErrorEntries), (2, log.WarningEntries) };
        int rowsChecked = 0;
        foreach ((int index, IReadOnlyList<FileJobLogEntryViewModel> entries) in tabs)
        {
            log.SelectedTabIndex = index;
            session.RenderUntil(() => ListOf(view, entries) is { } shown && RowsShown(shown, entries.Count), $"tab {index} rows shown");
            ItemsControl rows = ListOf(view, entries).Should().NotBeNull().And.BeAssignableTo<ItemsControl>().Subject;
            foreach (ContentPresenter presenter in rows.GetRealizedContainers().OfType<ContentPresenter>())
            {
                FileJobLogEntryViewModel entry = presenter.DataContext.Should().BeOfType<FileJobLogEntryViewModel>().Subject;
                Border row = presenter.Child.Should().BeOfType<Border>().Subject;
                Grid grid = row.Child.Should().BeOfType<Grid>().Subject;
                TextBlock first = grid.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).First();
                object? tip = ToolTip.GetTip(row);
                string expectedTip = string.Join(Environment.NewLine, entry.NodeLog);
                _output.WriteLine($"[tab {index}] {entry.FileName}: first='{first.Text}' timestamp='{entry.Timestamp}' tip='{tip?.ToString()?.Replace(Environment.NewLine, " | ", StringComparison.Ordinal)}'");
                first.Text.Should().Be(entry.Timestamp, "a row leads with the time its file finished");
                tip.Should().Be(expectedTip, "a row's tooltip lists what each node did to its file");
                rowsChecked++;
            }
        }

        _output.WriteLine($"rows checked={rowsChecked}");
        rowsChecked.Should().Be(3, "one row on each of the Output, Errors and Skipped tabs");
    }

    [AvaloniaFact]
    public void Log_lists_build_only_the_rows_in_view()
    {
        const int files = 3000;
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        log.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Enumerating));
        log.ReportProgressEvent(new FilesDiscovered(files));
        log.ReportProgressEvent(new PhaseChanged(ExecutionPhase.Processing));
        for (int i = 0; i < files; i++)
        {
            FileJob job = (i % 3) switch
            {
                0 => Job(session, $"file{i}.png", FileJobStatus.Succeeded),
                1 => Job(session, $"file{i}.png", FileJobStatus.Failed, "Unsupported image header"),
                _ => Job(session, $"file{i}.png", FileJobStatus.Skipped),
            };
            log.ReportProgressEvent(new FileProcessed(job));
        }

        ExecutionLogView view = LogView(session.Window);
        var tabs = new (int Index, IReadOnlyList<FileJobLogEntryViewModel> Entries)[] { (0, log.Entries), (1, log.ErrorEntries), (2, log.WarningEntries) };
        var realized = new Dictionary<int, int>();
        foreach ((int index, IReadOnlyList<FileJobLogEntryViewModel> entries) in tabs)
        {
            log.SelectedTabIndex = index;
            session.RenderUntil(
                () => ListOf(view, entries) is { } shown && shown.IsEffectivelyVisible && RowGrids(shown) is { Count: > 0 } grids && grids.All(g => g.Bounds.Height > 0),
                TimeSpan.FromSeconds(20),
                $"tab {index} rows shown");
            ItemsControl rows = ListOf(view, entries).Should().NotBeNull().And.BeAssignableTo<ItemsControl>().Subject;
            realized[index] = rows.GetRealizedContainers().Count();
            _output.WriteLine($"[tab {index}] entries={entries.Count} realized rows={realized[index]}");
        }

        realized.Values.Should().OnlyContain(count => count > 0 && count < 100, "a list builds the rows in view, not one per entry");
    }

    private static FileJob Job(VisualSession session, string name, FileJobStatus status, string? error = null)
    {
        string path = Path.Combine(session.Temp.Path, name);
        return new FileJob { OriginalPath = path, CurrentPath = path, Status = status, ErrorMessage = error };
    }

    private static ExecutionLogView LogView(Window window) =>
        window.GetVisualDescendants().OfType<ExecutionLogView>().Single();

    // The tab strip's toggles, by the tab index each one selects.
    private static ToggleButton Tab(ExecutionLogView view, string index) =>
        view.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.CommandParameter is string p && string.Equals(p, index, StringComparison.Ordinal));

    private static ItemsControl RowList(ExecutionLogView view, ExecutionLogViewModel log) =>
        view.GetVisualDescendants().OfType<ItemsControl>().Single(list => ReferenceEquals(list.ItemsSource, log.Entries));

    // A hidden tab's list joins the visual tree only once its scroll viewer is first laid out.
    private static ItemsControl? ListOf(ExecutionLogView view, object entries) =>
        view.GetVisualDescendants().OfType<ItemsControl>().SingleOrDefault(list => ReferenceEquals(list.ItemsSource, entries));

    // A row's presenter exists before its template content is built; only built rows count.
    private static List<Grid> RowGrids(ItemsControl rows) =>
        rows.GetRealizedContainers().OfType<ContentPresenter>()
            .Select(row => (row.Child as Border)?.Child).OfType<Grid>().ToList();

    private static bool RowsShown(ItemsControl rows, int count) =>
        rows.IsEffectivelyVisible && RowGrids(rows) is { } grids && grids.Count == count && grids.All(g => g.Bounds.Height > 0);

    private static Color ThemeColour(string key)
    {
        Application.Current!.TryGetResource(key, ThemeVariant.Dark, out object? value).Should().BeTrue($"the theme defines {key}");
        return value.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
    }

    private static bool WithinTolerance(Color actual, Color expected) =>
        Math.Abs(actual.R - expected.R) <= ColourTolerance
        && Math.Abs(actual.G - expected.G) <= ColourTolerance
        && Math.Abs(actual.B - expected.B) <= ColourTolerance;
}
