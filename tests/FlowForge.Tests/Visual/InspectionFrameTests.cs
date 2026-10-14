using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.Core.Settings;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// Frames kept for a person to look at: selection, the empty inspector, the running toolbar,
/// library search, a long pipeline name, fit to screen, the shortcuts dialog and the recent
/// pipelines flyout. Each checks only the view-model state that leads to its frame.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class InspectionFrameTests
{
    private const string Search = "resize";

    private readonly ITestOutputHelper _output;

    public InspectionFrameTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Clicked_station_shows_selection_glow()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        PipelineNodeViewModel station = editor.Nodes[0];

        session.Click(session.Stations().Single(c => ReferenceEquals(c.DataContext, station)));
        session.RenderUntil(
            () => ReferenceEquals(editor.SelectedNode, station) && session.InspectorReady(),
            "a station selected and its fields shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.EyesSelectionGlow);

        _output.WriteLine($"clicked={station.TypeKey} selected={editor.SelectedNode?.TypeKey}");
        editor.SelectedNode.Should().BeSameAs(station);
    }

    [AvaloniaFact]
    public void Fresh_window_shows_empty_inspector()
    {
        using var session = VisualSession.Open(_output);
        using WriteableBitmap frame = session.Capture(VisualFrames.EyesEmptyInspector);

        _output.WriteLine($"selected={session.ViewModel.Editor.SelectedNode?.TypeKey ?? "none"}");
        session.ViewModel.Editor.SelectedNode.Should().BeNull();
    }

    [AvaloniaFact]
    public void Toolbar_shows_running_state()
    {
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        log.IsRunning = true;
        try
        {
            using WriteableBitmap frame = session.Capture(VisualFrames.EyesToolbarRunning);

            _output.WriteLine($"running={log.IsRunning}");
            log.IsRunning.Should().BeTrue();
        }
        finally
        {
            log.IsRunning = false;
        }
    }

    [AvaloniaFact]
    public void Library_search_lists_only_matching_nodes()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        NodeLibraryViewModel library = session.ViewModel.NodeLibrary;
        TextBox searchBox = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.DataContext is NodeLibraryViewModel);

        session.Click(searchBox);
        window.KeyTextInput(Search);

        // Filtering replaces the shown groups; expand whichever remain so their rows are laid out.
        session.RenderUntil(
            () =>
            {
                ExpandLibraryGroups(window);
                List<NodeLibraryItemViewModel> rows = VisibleLibraryItems(window);
                return string.Equals(library.SearchText, Search, StringComparison.Ordinal)
                    && rows.Count > 0
                    && rows.All(item => item.DisplayName.Contains(Search, StringComparison.OrdinalIgnoreCase));
            },
            "library filtered to matching rows");
        using WriteableBitmap frame = session.Capture(VisualFrames.EyesLibrarySearch);

        var shown = VisibleLibraryItems(window).Select(item => item.DisplayName).ToList();
        var listed = library.Groups.SelectMany(group => group.Items).Select(item => item.DisplayName).ToList();
        _output.WriteLine($"search='{library.SearchText}' shown=[{string.Join(", ", shown)}] listed=[{string.Join(", ", listed)}]");
        library.SearchText.Should().Be(Search);
        shown.Should().NotBeEmpty().And.OnlyContain(name => name.Contains(Search, StringComparison.OrdinalIgnoreCase));
        listed.Should().NotBeEmpty().And.OnlyContain(name => name.Contains(Search, StringComparison.OrdinalIgnoreCase));
    }

    [AvaloniaFact]
    public async Task Opening_a_long_named_pipeline_puts_its_name_in_the_title()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        string baseName = string.Concat(Enumerable.Repeat("long-pipeline-title-", 6));
        string path = Path.Combine(session.Temp.Path, baseName + ".ffpipe");
        await PipelineSerializer.SaveAsync(PipelineTemplateLibrary.CreateFromTemplate(VisualSession.WebExportTemplate), path);

        await vm.OpenRecentCommand.ExecuteAsync(path);
        session.RenderUntil(() => session.Stations().Count == vm.Editor.Nodes.Count, "opened pipeline's stations laid out");
        using WriteableBitmap frame = session.Capture(VisualFrames.EyesLongTitle);

        _output.WriteLine($"base-name-length={baseName.Length} title='{vm.Title}'");
        vm.Title.Should().Contain(baseName);
    }

    [AvaloniaFact]
    public void Fit_to_screen_shows_every_template_station()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        MainWindow window = session.Window;
        session.LoadTemplate(VisualSession.WebExportTemplate);

        // Fitting measures the laid-out stations, so they must exist first.
        editor.RequestFitToScreen();
        NodifyEditor canvas = window.GetVisualDescendants().OfType<NodifyEditor>().Single(e => string.Equals(e.Name, "Editor", StringComparison.Ordinal));
        var visible = new Rect(canvas.Bounds.Size);

        // Both corners go through the zoom transform; a station's own bounds are unscaled.
        session.RenderUntil(
            () => session.Stations().All(s =>
                visible.Contains(s.TranslatePoint(default, canvas)!.Value)
                && visible.Contains(s.TranslatePoint(new Point(s.Bounds.Width, s.Bounds.Height), canvas)!.Value)),
            "every template station fitted on screen");
        using WriteableBitmap frame = session.Capture(VisualFrames.EyesFit);

        int templateNodes = PipelineTemplateLibrary.CreateFromTemplate(VisualSession.WebExportTemplate).Nodes.Count;
        _output.WriteLine($"nodes={editor.Nodes.Count} template={templateNodes} zoom={canvas.ViewportZoom}");
        editor.Nodes.Should().HaveCount(templateNodes);
    }

    [AvaloniaFact]
    public void F1_opens_the_shortcuts_dialog()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;

        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, keySymbol: null);
        window.KeyRelease(Key.F1, RawInputModifiers.None, PhysicalKey.F1, keySymbol: null);
        session.RenderUntil(
            () => window.OwnedWindows.OfType<ShortcutsWindow>().Any(dialog => dialog.IsVisible && dialog.Bounds.Width > 0),
            "shortcuts dialog shown");
        ShortcutsWindow dialog = window.OwnedWindows.OfType<ShortcutsWindow>().Single();
        int owned = window.OwnedWindows.Count;
        try
        {
            using WriteableBitmap frame = session.Capture(VisualFrames.EyesShortcutsDialog, dialog);

            _output.WriteLine($"owned-windows={owned} dialog={dialog.GetType().Name} size={frame.PixelSize}");
            owned.Should().Be(1, "F1 opens one dialog owned by the main window");
        }
        finally
        {
            dialog.Close();
            session.RenderUntil(() => window.OwnedWindows.Count == 0, "shortcuts dialog closed");
        }
    }

    [AvaloniaFact]
    public async Task Recent_button_lists_the_saved_pipeline()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        string recent = Path.Combine(session.Temp.Path, "recent.ffpipe");
        await PipelineSerializer.SaveAsync(PipelineTemplateLibrary.CreateFromTemplate(VisualSession.WebExportTemplate), recent);
        var settings = new AppSettings();
        settings.AddRecentPipeline(recent);
        await new AppSettingsManager(session.SettingsPath, NullLogger<AppSettingsManager>.Instance).SaveAsync(settings);

        Task reread = vm.InitializeAsync();
        session.RenderUntil(() => reread.IsCompleted, "settings re-read");
        await reread;

        Button recentButton = session.Window.GetVisualDescendants().OfType<Button>()
            .Single(button => string.Equals(button.Name, "RecentButton", StringComparison.Ordinal));
        var flyout = (MenuFlyout)recentButton.Flyout!;
        session.Click(recentButton);
        session.RenderUntil(() => flyout.IsOpen, "recent flyout open");
        try
        {
            // The flyout draws in its own popup root, so this frame shows the window behind it.
            using WriteableBitmap frame = session.Capture(VisualFrames.EyesRecentFlyout);

            _output.WriteLine($"recent-items={vm.RecentPipelineItems.Count} files=[{string.Join(", ", vm.RecentPipelineItems.Select(item => item.FileName))}] flyout-items={flyout.Items.Count}");
            vm.RecentPipelineItems.Should().ContainSingle().Which.FullPath.Should().Be(recent);
        }
        finally
        {
            flyout.Hide();
            session.RenderUntil(() => !flyout.IsOpen, "recent flyout closed");
        }
    }

    private static void ExpandLibraryGroups(Window window)
    {
        foreach (TreeViewItem group in window.GetVisualDescendants().OfType<TreeViewItem>().Where(item => item.DataContext is NodeLibraryGroupViewModel))
        {
            group.IsExpanded = true;
        }
    }

    private static List<NodeLibraryItemViewModel> VisibleLibraryItems(Window window) =>
        window.GetVisualDescendants().OfType<TreeViewItem>()
            .Where(row => row.IsEffectivelyVisible && row.Bounds.Height > 0)
            .Select(row => row.DataContext)
            .OfType<NodeLibraryItemViewModel>()
            .ToList();
}
