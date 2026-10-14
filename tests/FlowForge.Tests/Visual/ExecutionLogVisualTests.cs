using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
            foreach (FileJob job in jobs)
            {
                log.ReportProgressEvent(new FileProcessed(job));
            }

            ExecutionLogView view = LogView(session.Window);
            TextBlock succeeded = TabCount(view, "0");
            TextBlock failed = TabCount(view, "1");
            TextBlock currentFile = CurrentFileText(view);
            session.Pump();
            ProgressBar bar = view.GetVisualDescendants().OfType<ProgressBar>().Single(p => !p.IsIndeterminate);
            session.RenderUntil(
                () => new Control[] { succeeded, failed, bar, currentFile }.All(c => c.IsEffectivelyVisible && c.Bounds.Width > 0),
                "log strip shows the fed counts");
            using WriteableBitmap frame = session.Capture(VisualFrames.LogProgress);

            _output.WriteLine($"succeeded={succeeded.Text} failed={failed.Text} progress={bar.Value} current={currentFile.Text}");
            succeeded.Text.Should().Be("2");
            failed.Text.Should().Be("1");
            bar.Value.Should().BeApproximately(75, 0.001, "three of four discovered files are processed");
            currentFile.Text.Should().Be(jobs[^1].FileName);
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
        session.RenderUntil(() => RowsShown(rows, 3), "three log rows shown");

        // Pressing the open tab collapses the row list; pressing it again expands it.
        Click(window, CentreInWindow(outputTab));
        session.RenderUntil(() => !log.IsPanelExpanded && !rows.IsEffectivelyVisible, "row list collapsed");
        Click(window, CentreInWindow(outputTab));
        session.RenderUntil(() => log.IsPanelExpanded && RowsShown(rows, 3), "row list expanded");
        using WriteableBitmap frame = session.Capture(VisualFrames.LogRows);

        Grid failedRow = RowGrids(rows).Single(g => g.DataContext is FileJobLogEntryViewModel { IsError: true });
        TextBlock error = failedRow.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 3);
        Ellipse marker = failedRow.Children.OfType<Ellipse>().Single(e => e.IsEffectivelyVisible);
        PixelRect markerBounds = session.BoundsInWindow(marker);
        var markerBand = new PixelRect(markerBounds.X + (markerBounds.Width / 2) - 1, markerBounds.Y + (markerBounds.Height / 2) - 1, 2, 2);

        // Inside the row's left padding, beside the marker: background only, no text.
        var backgroundBand = new PixelRect(markerBounds.X - 6, markerBand.Y, 2, 2);
        Color measured = session.MeanColour(frame, markerBand);
        Color background = session.MeanColour(frame, backgroundBand);
        Color expected = ThemeColour("ForgeError");
        _output.WriteLine($"rows={RowGrids(rows).Count} error='{error.Text}' visible={error.IsEffectivelyVisible}");
        _output.WriteLine($"marker band={markerBand} measured={measured} expected={expected} background={background} tolerance={ColourTolerance}");
        RowGrids(rows).Should().HaveCount(3, "each processed file adds a row");
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
        TextBlock summary = SummaryText(view);
        session.RenderUntil(() => RowsShown(rows, 3) && summary.IsEffectivelyVisible && summary.Bounds.Width > 0, "run rows and summary shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.RealRun);

        _output.WriteLine($"succeeded={log.Succeeded} failed={log.Failed} rows={RowGrids(rows).Count} summary='{summary.Text}'");
        log.Succeeded.Should().Be(3);
        log.Failed.Should().Be(0);
        RowGrids(rows).Should().HaveCount(3, "each copied file adds a row");
        log.Summary.Should().NotBeNullOrEmpty();
        summary.Text.Should().Be(log.Summary);
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

    // A tab's count badge: the one text inside a border, beside the tab's label.
    private static TextBlock TabCount(ExecutionLogView view, string index) =>
        Tab(view, index).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Parent is Border);

    // The text after the strip's "Processing" label.
    private static TextBlock CurrentFileText(ExecutionLogView view)
    {
        TextBlock label = view.GetVisualDescendants().OfType<TextBlock>().Single(t => string.Equals(t.Text, "Processing", StringComparison.Ordinal));
        return (TextBlock)((StackPanel)label.Parent!).Children[1];
    }

    // The tab strip's one text of its own, right of the tabs.
    private static TextBlock SummaryText(ExecutionLogView view) =>
        ((Grid)Tab(view, "0").Parent!).Children.OfType<TextBlock>().Single();

    private static ItemsControl RowList(ExecutionLogView view, ExecutionLogViewModel log) =>
        view.GetVisualDescendants().OfType<ItemsControl>().Single(list => ReferenceEquals(list.ItemsSource, log.Entries));

    // A row's presenter exists before its template content is built; only built rows count.
    private static List<Grid> RowGrids(ItemsControl rows) =>
        rows.GetRealizedContainers().OfType<ContentPresenter>()
            .Select(row => (row.Child as Border)?.Child).OfType<Grid>().ToList();

    private static bool RowsShown(ItemsControl rows, int count) =>
        rows.IsEffectivelyVisible && RowGrids(rows) is { } grids && grids.Count == count && grids.All(g => g.Bounds.Height > 0);

    private static Point CentreInWindow(Avalonia.Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");

    private static void Click(TopLevel topLevel, Point point)
    {
        topLevel.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        topLevel.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

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
