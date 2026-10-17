using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.VisualTree;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nodify.Avalonia;
using Nodify.Avalonia.Connections;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The editor's core gestures, driven by pointer and keyboard input against the real window:
/// drag from the library, connect two ports, select a station, edit its fields.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class EditorInteractionTests
{
    private const string EditedPattern = "{name}_edited{ext}";

    private readonly ITestOutputHelper _output;

    public EditorInteractionTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string> RegisteredTypeKeys { get; } = new(
        NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetRegisteredTypeKeys().Order(StringComparer.Ordinal));

    [AvaloniaFact]
    public void Library_drag_adds_node_at_drop_point()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        foreach (TreeViewItem group in window.GetVisualDescendants().OfType<TreeViewItem>().ToList())
        {
            group.IsExpanded = true;
        }

        session.RenderUntil(() => LibraryRows(window).Any(row => row.Bounds.Height > 0), "library rows shown");
        TreeViewItem row = LibraryRows(window).First();
        string typeKey = ((NodeLibraryItemViewModel)row.DataContext!).TypeKey;
        NodifyEditor editor = session.Canvas();
        Point start = session.CentreOf(row);
        Point drop = InWindow(editor, new Point(editor.Bounds.Width * 0.4, editor.Bounds.Height * 0.3));

        window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(start + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
        session.Pump();
        window.MouseMove(drop, RawInputModifiers.LeftMouseButton);
        session.Pump();
        window.MouseUp(drop, MouseButton.Left, RawInputModifiers.None);
        session.RenderUntil(() => session.ViewModel.Editor.Nodes.Count == 1, "library drop adds a node");
        using WriteableBitmap frame = session.Capture(VisualFrames.LibraryDrag);

        // The drop handler's own mapping: the point in editor coordinates over the zoom, plus the viewport origin.
        Point inEditor = window.TranslatePoint(drop, editor)!.Value;
        var expected = new Point(
            (inEditor.X / editor.ViewportZoom) + editor.ViewportLocation.X,
            (inEditor.Y / editor.ViewportZoom) + editor.ViewportLocation.Y);
        PipelineNodeViewModel node = session.ViewModel.Editor.Nodes.Single();
        _output.WriteLine($"row={typeKey} drop={drop} zoom={editor.ViewportZoom} viewport={editor.ViewportLocation}");
        _output.WriteLine($"location measured={node.Location} expected={expected}");
        node.TypeKey.Should().Be(typeKey);
        node.Location.X.Should().BeApproximately(expected.X, 0.5);
        node.Location.Y.Should().BeApproximately(expected.Y, 0.5);
    }

    [AvaloniaFact]
    public void Connector_drag_connects_and_refuses_same_direction_self_and_duplicate()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("RenamePattern", new Point(80, 120), session.ViewModel.Registry);
        editor.AddNode("Sort", new Point(440, 120), session.ViewModel.Registry);
        PipelineNodeViewModel a = editor.Nodes[0];
        PipelineNodeViewModel b = editor.Nodes[1];
        Connector aOut = session.WaitForPort(a.Output[0]);
        Connector aIn = session.WaitForPort(a.Input[0]);
        Connector bOut = session.WaitForPort(b.Output[0]);
        Connector bIn = session.WaitForPort(b.Input[0]);

        DragBetween(session, aOut, bIn, () =>
        {
            using WriteableBitmap pending = session.Capture(VisualFrames.ConnectPending);
            _output.WriteLine($"pending visible={editor.PendingConnection.IsVisible} source={editor.PendingConnection.Source?.Title}");
            editor.PendingConnection.IsVisible.Should().BeTrue("a drag from an output port shows the pending wire");
            editor.PendingConnection.Source.Should().BeSameAs(a.Output[0]);
        });
        using (WriteableBitmap connected = session.Capture(VisualFrames.ConnectDone))
        {
            editor.Connections.Should().ContainSingle();
            editor.Connections[0].Source.Should().BeSameAs(a.Output[0]);
            editor.Connections[0].Target.Should().BeSameAs(b.Input[0]);
        }

        int refusals = 0;
        foreach ((string attempt, Connector from, Connector to) in new[]
        {
            ("same-direction", aOut, bOut),
            ("self", aOut, aIn),
            ("duplicate", aOut, bIn),
        })
        {
            // The wire must be live mid-drag, so an unchanged count is a refusal, not a drag that never started.
            bool started = false;
            DragBetween(session, from, to, () =>
                started = editor.PendingConnection.IsVisible && ReferenceEquals(editor.PendingConnection.Source, from.DataContext));
            _output.WriteLine($"{attempt}: started={started} connections={editor.Connections.Count} pending={editor.PendingConnection.IsVisible}");
            started.Should().BeTrue($"the {attempt} drag starts a pending wire");
            editor.Connections.Should().HaveCount(1, $"the {attempt} connection is refused");
            editor.PendingConnection.IsVisible.Should().BeFalse();
            refusals++;
        }

        _output.WriteLine($"refusals={refusals}");
    }

    [AvaloniaTheory]
    [MemberData(nameof(RegisteredTypeKeys))]
    public void Selecting_each_node_type_shows_one_labelled_editor_per_field(string typeKey)
    {
        using var session = VisualSession.Open(_output);
        NodeRegistry registry = session.ViewModel.Registry;
        session.ViewModel.Editor.AddNode(typeKey, new Point(200, 160), registry);
        Select(session, session.ViewModel.Editor.Nodes.Single());
        using WriteableBitmap frame = session.Capture(VisualFrames.Inspector(typeKey));

        IReadOnlyList<ConfigField> schema = registry.GetConfigSchema(typeKey);
        List<ContentPresenter> cards = session.InspectorCards();
        _output.WriteLine($"type-keys={RegisteredTypeKeys.Count}");
        session.ViewModel.Editor.SelectedNode!.TypeKey.Should().Be(typeKey);
        cards.Should().HaveCount(schema.Count, "the inspector shows one card per schema field");
        for (int i = 0; i < schema.Count; i++)
        {
            ConfigField field = schema[i];
            Control[] parts = ((cards[i].Child as Border)?.Child as StackPanel)?.Children.ToArray() ?? [];
            string expectedLabel = field.Required ? field.Label + " *" : field.Label;
            _output.WriteLine($"field {field.Key} {field.Type}: {string.Join(" | ", parts.Select(Describe))}");
            parts.Should().HaveCount(2, $"the {field.Key} card holds its label and one editor");
            parts[0].Should().BeOfType<TextBlock>().Which.Text.Should().Be(expectedLabel);
            parts[1].Should().BeOfType(EditorTypeFor(field.Type), $"{field.Key} is a {field.Type} field");
        }

        _output.WriteLine($"fields-checked={schema.Count}");
    }

    // A station's title and subtitle are trimmed to its text column; hovering either shows it in full.
    [AvaloniaFact]
    public void Station_title_and_subtitle_show_their_full_text_on_hover()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        NodeRegistry registry = session.ViewModel.Registry;
        var typeKeys = registry.GetRegisteredTypeKeys().Order(StringComparer.Ordinal).ToList();
        for (int i = 0; i < typeKeys.Count; i++)
        {
            editor.AddNode(typeKeys[i], new Point(20 + (220 * (i % 4)), 20 + (150 * (i / 4))), registry);
        }

        (TextBlock Title, TextBlock Subtitle)? hovered = null;
        foreach (PipelineNodeViewModel node in editor.Nodes)
        {
            ItemContainer station = session.WaitForStation(node);
            string displayName = registry.GetDisplayName(node.TypeKey);
            TextBlock title = station.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => string.Equals(text.Text, displayName, StringComparison.Ordinal));
            TextBlock subtitle = station.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Classes.Contains("mw-sub"));
            bool trimmed = title.TextLayout.TextLines.Any(line => line.HasCollapsed);
            _output.WriteLine(
                $"{node.TypeKey}: title '{displayName}' {FullWidth(title):F1} px in a {title.Bounds.Width:F1} px column trimmed={trimmed}"
                + $" | subtitle '{node.MwSub}' {FullWidth(subtitle):F1} px in {subtitle.Bounds.Width:F1}"
                + $" | tips '{ToolTip.GetTip(title)}' / '{ToolTip.GetTip(subtitle)}'");
            ToolTip.GetTip(title).Should().Be(displayName, $"{node.TypeKey}'s title tooltip is its full display name");
            subtitle.Text.Should().Be(node.MwSub);
            ToolTip.GetTip(subtitle).Should().Be(node.MwSub, $"{node.TypeKey}'s subtitle tooltip is its full subtitle");
            if (trimmed && hovered is null)
            {
                hovered = (title, subtitle);
            }
        }

        _output.WriteLine($"types-checked={editor.Nodes.Count} registered={typeKeys.Count}");
        editor.Nodes.Should().HaveCount(typeKeys.Count);
        hovered.Should().NotBeNull("at least one title is trimmed to the column");
        foreach (TextBlock text in new[] { hovered!.Value.Title, hovered.Value.Subtitle })
        {
            session.Window.MouseMove(session.CentreOf(text), RawInputModifiers.None);
            session.RenderUntil(() => ToolTip.GetIsOpen(text), $"the tooltip of '{text.Text}' opens on hover");
        }
    }

    [AvaloniaFact]
    public async Task Edited_fields_survive_save_and_reload()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("RenamePattern", new Point(80, 120), session.ViewModel.Registry);
        editor.AddNode("FolderOutput", new Point(440, 120), session.ViewModel.Registry);
        editor.AddNode("ImageResize", new Point(260, 300), session.ViewModel.Registry);
        PipelineNodeViewModel rename = editor.Nodes[0];
        PipelineNodeViewModel output = editor.Nodes[1];
        PipelineNodeViewModel resize = editor.Nodes[2];
        resize.Config["width"] = JsonSerializer.SerializeToElement(1920);

        Select(session, rename);
        TextBox pattern = FieldEditor<TextBox>(session, "pattern");
        EditField(session, "pattern", () =>
        {
            session.Click(pattern);
            session.Window.KeyTextInput(EditedPattern);
        });
        bool patternFocused = pattern.IsFocused;
        EditField(session, "startIndex", () =>
        {
            session.Click(FieldEditor<TextBox>(session, "startIndex"));
            session.Press("Ctrl+A");
            session.Window.KeyTextInput("7");
        });
        Select(session, resize);
        EditField(session, "width", () =>
        {
            session.Click(FieldEditor<TextBox>(session, "width"));
            session.Press("Ctrl+A");
            session.Press("Back");
        });
        Select(session, output);
        EditField(session, "overwrite", () => session.Click(FieldEditor<CheckBox>(session, "overwrite")));
        EditField(session, "mode", () => PickOption(session, FieldEditor<ComboBox>(session, "mode"), "move"));
        using WriteableBitmap frame = session.Capture(VisualFrames.EditPersists);

        _output.WriteLine($"pattern field focused after its edit={patternFocused}");
        patternFocused.Should().BeTrue("the field keeps the focus through the edit it sends");

        // A null value is a key the edit removed: an emptied Int box clears the setting.
        (PipelineNodeViewModel Node, string Key, string? Expected)[] edits =
        [
            (rename, "pattern", EditedPattern),
            (rename, "startIndex", "7"),
            (resize, "width", null),
            (output, "overwrite", "true"),
            (output, "mode", "move"),
        ];
        foreach ((PipelineNodeViewModel node, string key, string? expected) in edits)
        {
            ValueText(node.Config, key).Should().Be(expected, $"typing into {key} updates the node's config");
        }

        string path = Path.Combine(session.Temp.Path, "edit.ffpipe");
        await PipelineSerializer.SaveAsync(editor.BuildGraph(), path);
        PipelineGraph reloaded = await PipelineSerializer.LoadAsync(path);
        foreach ((PipelineNodeViewModel node, string key, string? expected) in edits)
        {
            string? value = ValueText(reloaded.Nodes.Single(n => n.Id == node.Id).Config, key);
            _output.WriteLine($"reloaded {node.TypeKey}.{key}={value}");
            value.Should().Be(expected, $"{key} survives a save and reload");
        }
    }

    [AvaloniaFact]
    public void Typing_into_a_field_keeps_its_focus_so_Delete_edits_the_text()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("RenamePattern", new Point(80, 120), session.ViewModel.Registry);
        PipelineNodeViewModel rename = editor.Nodes[0];
        TextBox box = FocusField(session, rename, "pattern");
        window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
        window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");

        // Two keystrokes, each committed and the queued work run before the next.
        foreach (string text in new[] { "a", "b" })
        {
            window.KeyTextInput(text);
            session.Pump();
            _output.WriteLine($"after '{text}': pattern='{ValueText(rename.Config, "pattern")}' focused={Focused(window)}");
        }

        string? typed = ValueText(rename.Config, "pattern");
        bool kept = box.IsFocused;
        int before = editor.Nodes.Count;
        session.Press("Delete");
        session.Pump();
        _output.WriteLine($"after Delete: nodes {before} -> {editor.Nodes.Count}");

        typed.Should().Be("ab", "each keystroke lands in the field");
        kept.Should().BeTrue("the field keeps the focus through its own edits");
        editor.Nodes.Should().HaveCount(before, "Delete in a focused field edits its text, not the canvas");
        editor.Nodes.Should().Contain(rename);
    }

    [AvaloniaFact]
    public void Saving_keeps_the_focused_field_so_Delete_edits_the_text()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        EditorViewModel editor = vm.Editor;
        editor.AddNode("RenamePattern", new Point(80, 120), vm.Registry);
        PipelineNodeViewModel rename = editor.Nodes[0];
        string path = Path.Combine(session.Temp.Path, "focused.ffpipe");
        vm.CurrentFilePath = path;
        TextBox box = FocusField(session, rename, "pattern");
        bool dirty = vm.IsDirty;

        session.Press("Ctrl+S");
        session.RenderUntil(() => !vm.IsDirty, "the pipeline saved");
        session.Pump();
        bool kept = box.IsFocused;
        int before = editor.Nodes.Count;
        session.Press("Delete");
        session.Pump();
        _output.WriteLine($"saved={File.Exists(path)} focused after save={Focused(session.Window)} nodes {before} -> {editor.Nodes.Count}");

        dirty.Should().BeTrue("the added station is an unsaved change");
        kept.Should().BeTrue("a save leaves the focused field focused");
        editor.Nodes.Should().HaveCount(before, "Delete in a focused field edits its text, not the canvas");
        editor.Nodes.Should().Contain(rename);
    }

    [AvaloniaFact]
    public void Delete_and_undo_restore_the_selected_station()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        EditorViewModel editor = session.ViewModel.Editor;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        PipelineNodeViewModel station = editor.Nodes[1];
        int before = editor.Nodes.Count;

        session.Click(session.WaitForStation(station));
        session.RenderUntil(() => editor.SelectedNode is not null, "a station selected");
        PipelineNodeViewModel? selected = editor.SelectedNode;
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, keySymbol: null);
        window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, keySymbol: null);
        session.Pump();
        int afterDelete = editor.Nodes.Count;
        bool deletedThatStation = !editor.Nodes.Contains(station);
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        window.KeyRelease(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, "z");
        session.Pump();
        int afterUndo = editor.Nodes.Count;
        using WriteableBitmap frame = session.Capture(VisualFrames.SelectDeleteUndo);

        _output.WriteLine($"clicked={station.TypeKey} selected={selected?.TypeKey} nodes before={before} delete={afterDelete} undo={afterUndo}");
        selected.Should().BeSameAs(station, "the click selects the station under the pointer");
        afterDelete.Should().Be(before - 1);
        deletedThatStation.Should().BeTrue("Delete removes the selected station");
        afterUndo.Should().Be(before);
        editor.Nodes.Should().Contain(station, "undo restores the deleted station");
    }

    [AvaloniaFact]
    public void Dragging_a_station_moves_it_by_the_drag_over_zoom()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        EditorViewModel editor = session.ViewModel.Editor;

        // Grid-aligned start (cells are 40 wide), so grid snapping cannot shift the result.
        var start = new Point(200, 160);
        editor.AddNode("RenamePattern", start, session.ViewModel.Registry);
        PipelineNodeViewModel node = editor.Nodes.Single();
        NodifyEditor canvas = session.Canvas();
        Vector delta = new Vector(80, 40) * canvas.ViewportZoom;
        Point from = session.CentreOf(session.WaitForStation(node));

        // A real pointer reaches the station before pressing; the editor reads that last pointer
        // event when a station drag begins, and has none if the press is the first event it sees.
        window.MouseMove(from, RawInputModifiers.None);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        for (int step = 1; step <= 4; step++)
        {
            window.MouseMove(from + (delta * step / 4), RawInputModifiers.LeftMouseButton);
            session.Pump();
        }

        window.MouseUp(from + delta, MouseButton.Left, RawInputModifiers.None);
        session.Pump();
        using WriteableBitmap frame = session.Capture(VisualFrames.MoveStation);

        Point expected = start + (delta / canvas.ViewportZoom);
        _output.WriteLine($"zoom={canvas.ViewportZoom} delta={delta} location measured={node.Location} expected={expected}");
        node.Location.X.Should().BeApproximately(expected.X, 0.5);
        node.Location.Y.Should().BeApproximately(expected.Y, 0.5);
    }

    [AvaloniaFact]
    public void Template_stations_render_title_icon_and_connectors()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;

        // Fitting measures the laid-out stations, so they must exist first.
        session.LoadTemplate(VisualSession.WebExportTemplate);
        editor.RequestFitToScreen();
        session.RenderUntil(session.StationsInsideCanvas, "every template station fitted on screen");
        using WriteableBitmap frame = session.Capture(VisualFrames.NodeRender);

        List<ItemContainer> stations = session.Stations();
        foreach (ItemContainer station in stations)
        {
            var node = (PipelineNodeViewModel)station.DataContext!;
            var parts = station.GetVisualDescendants().ToList();
            int titles = parts.OfType<TextBlock>().Count(t => string.Equals(t.Text, node.Title, StringComparison.Ordinal));
            int icons = parts.OfType<Avalonia.Controls.Shapes.Path>().Count(p => p.Data is not null && p.IsEffectivelyVisible);
            int inputs = parts.OfType<Connector>().Count(c => c.DataContext is PipelineConnectorViewModel { IsInput: true });
            int outputs = parts.OfType<Connector>().Count(c => c.DataContext is PipelineConnectorViewModel { IsInput: false });
            _output.WriteLine($"{node.TypeKey} '{node.Title}': titles={titles} icons={icons} inputs={inputs}/{node.Input.Count} outputs={outputs}/{node.Output.Count}");
            titles.Should().Be(1, $"{node.TypeKey} shows its title once");
            icons.Should().BeGreaterThan(0, $"{node.TypeKey} shows its icon");
            inputs.Should().Be(node.Input.Count, $"{node.TypeKey} shows an input port for each input");
            outputs.Should().Be(node.Output.Count, $"{node.TypeKey} shows an output port for each output");
        }

        _output.WriteLine($"stations-checked={stations.Count}");
        stations.Should().HaveCount(editor.Nodes.Count);
    }

    [AvaloniaFact]
    public void Wheel_zooms_within_its_range_and_middle_or_right_drag_pans()
    {
        using var session = VisualSession.Open(_output);
        session.LoadTemplate(VisualSession.WebExportTemplate);
        NodifyEditor canvas = session.Canvas();
        Point centre = session.CentreOf(canvas);
        _output.WriteLine($"range min={canvas.MinViewportZoom} max={canvas.MaxViewportZoom} start zoom={canvas.ViewportZoom}");

        double zoomedIn = WheelUntilSteady(session, canvas, centre, 1);
        double zoomedOut = WheelUntilSteady(session, canvas, centre, -1);

        var by = new Vector(120, 80);
        var pans = new List<(MouseButton Button, Point Before, Point After)>();
        foreach (MouseButton button in new[] { MouseButton.Middle, MouseButton.Right })
        {
            Point before = canvas.ViewportLocation;
            session.Drag(session.OpenCanvasPoint(), by, button);
            pans.Add((button, before, canvas.ViewportLocation));
            _output.WriteLine($"{button}-drag by {by} at zoom {canvas.ViewportZoom}: viewport {before} -> {canvas.ViewportLocation}");
        }

        using WriteableBitmap frame = session.Capture(VisualFrames.CanvasPanZoom);
        canvas.MinViewportZoom.Should().Be(0.25);
        canvas.MaxViewportZoom.Should().Be(4);
        zoomedIn.Should().BeApproximately(4, 1e-9, "the wheel zooms in up to 400%");
        zoomedOut.Should().BeApproximately(0.25, 1e-9, "the wheel zooms out down to 25%");
        foreach ((MouseButton button, Point before, Point after) in pans)
        {
            // The canvas follows the pointer: the view moves back by the drag over the zoom.
            Point expected = before - (by / canvas.ViewportZoom);
            after.X.Should().BeApproximately(expected.X, 0.5, $"a {button}-drag pans the canvas");
            after.Y.Should().BeApproximately(expected.Y, 0.5, $"a {button}-drag pans the canvas");
        }
    }

    [AvaloniaFact]
    public void Rubber_band_and_modifier_clicks_select_as_the_editor_defines()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        PipelineNodeViewModel[] nodes = editor.Nodes.Take(3).ToArray();
        ItemContainer[] stations = nodes.Select(session.WaitForStation).ToArray();
        PixelRect first = session.BoundsInWindow(stations[0]);
        PixelRect second = session.BoundsInWindow(stations[1]);
        PixelRect third = session.BoundsInWindow(stations[2]);

        // The band starts on open canvas above and left of the first station and ends in the gap
        // after the second, below it: it holds the first two stations whole and none of the third.
        var from = new Point(first.X - 30, first.Y - 30);
        var to = new Point((second.Right + third.X) / 2.0, second.Bottom + 30);
        session.Drag(from, to - from, MouseButton.Left);
        var steps = new List<string> { $"band {from}-{to}: {Selected(editor)}" };
        foreach ((string gesture, int station, RawInputModifiers modifiers) in new[]
        {
            ("Shift+click", 2, RawInputModifiers.Shift),
            ("Ctrl+click", 0, RawInputModifiers.Control),
            ("Alt+click", 1, RawInputModifiers.Alt),
            ("click", 0, RawInputModifiers.None),
        })
        {
            session.Click(stations[station], modifiers);
            session.Pump();
            steps.Add($"{gesture} {nodes[station].TypeKey}: {Selected(editor)}");
        }

        using WriteableBitmap frame = session.Capture(VisualFrames.CanvasMultiSelect);
        steps.ForEach(_output.WriteLine);

        // The editor's own selection rules: Shift adds, Ctrl toggles, Alt removes, a plain click replaces.
        steps.Select(step => step[(step.IndexOf(':', StringComparison.Ordinal) + 2)..]).Should().Equal(
            "FolderInput, Filter",
            "FolderInput, Filter, ImageResize",
            "Filter, ImageResize",
            "ImageResize",
            "FolderInput");
    }

    private static IEnumerable<TreeViewItem> LibraryRows(Window window) =>
        window.GetVisualDescendants().OfType<TreeViewItem>().Where(item => item.DataContext is NodeLibraryItemViewModel);

    private static Point InWindow(Avalonia.Visual visual, Point point) =>
        visual.TranslatePoint(point, TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");

    // Press on one port, move halfway (where midway runs), move onto the other port and release there.
    private static void DragBetween(VisualSession session, Connector from, Connector to, Action? midway = null)
    {
        Point start = session.CentreOf(from);
        Point end = session.CentreOf(to);
        session.Window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
        session.Window.MouseMove(start + ((end - start) / 2), RawInputModifiers.LeftMouseButton);
        session.Pump();
        midway?.Invoke();
        session.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        session.Pump();
        session.Window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
        session.Pump();
    }

    // Clicks the station's body, away from its ports, and waits for the inspector to show its fields.
    private static void Select(VisualSession session, PipelineNodeViewModel node)
    {
        session.Click(session.WaitForStation(node));
        session.RenderUntil(
            () => ReferenceEquals(session.ViewModel.Editor.SelectedNode, node) && session.InspectorReady(),
            $"{node.TypeKey} selected and its fields shown");
    }

    private static T FieldEditor<T>(VisualSession session, string key)
        where T : Control =>
        session.InspectorCards()
            .Single(card => card.DataContext is ConfigFieldViewModel field && string.Equals(field.Key, key, StringComparison.Ordinal))
            .GetVisualDescendants().OfType<T>().First();

    // Waits for the edit to reach the selected station's config before the next gesture.
    private static void EditField(VisualSession session, string key, Action input)
    {
        IDictionary<string, JsonElement> config = session.ViewModel.Editor.SelectedNode!.Config;
        string? before = ValueText(config, key);
        input();
        session.RenderUntil(
            () => !string.Equals(ValueText(config, key), before, StringComparison.Ordinal) && session.InspectorReady(),
            $"{key} edited");
    }

    // Selects the station, clicks the field's text box and waits for it to take the keyboard focus.
    private static TextBox FocusField(VisualSession session, PipelineNodeViewModel node, string key)
    {
        Select(session, node);
        TextBox box = FieldEditor<TextBox>(session, key);
        session.Click(box);
        session.RenderUntil(() => box.IsFocused, $"the {key} field focused");
        return box;
    }

    // Turns the wheel one notch at a time until a notch leaves the zoom where it was; returns that zoom.
    private double WheelUntilSteady(VisualSession session, NodifyEditor canvas, Point at, int notch)
    {
        double previous;
        int notches = 0;
        do
        {
            previous = canvas.ViewportZoom;
            session.Window.MouseWheel(at, new Vector(0, notch), RawInputModifiers.None);
            session.Pump();
            notches++;
        }
        while (canvas.ViewportZoom != previous && notches < 200);

        _output.WriteLine($"wheel {(notch > 0 ? "in" : "out")}: {notches} notches, zoom stops at {canvas.ViewportZoom}");
        return canvas.ViewportZoom;
    }

    private static string Selected(EditorViewModel editor) =>
        string.Join(", ", editor.Nodes.Where(node => node.IsSelected).Select(node => node.TypeKey));

    private static string Focused(TopLevel topLevel) =>
        topLevel.FocusManager?.GetFocusedElement() is Control control ? control.GetType().Name : "nothing";

    // Opens the drop-down with a click and clicks the option in its popup.
    private static void PickOption(VisualSession session, ComboBox comboBox, string option)
    {
        session.Click(comboBox);
        ComboBoxItem? Find() => comboBox.GetRealizedContainers().OfType<ComboBoxItem>()
            .SingleOrDefault(item => string.Equals(item.Content as string, option, StringComparison.Ordinal) && item.Bounds.Width > 0);
        session.RenderUntil(() => comboBox.IsDropDownOpen && Find() is not null, $"option '{option}' shown");
        ComboBoxItem target = Find()!;
        session.Click(target);
    }

    private static string? ValueText(IDictionary<string, JsonElement> config, string key) =>
        !config.TryGetValue(key, out JsonElement value) ? null
            : value.ValueKind == JsonValueKind.String ? value.GetString()
            : value.GetRawText();

    private static Type EditorTypeFor(ConfigFieldType type) => type switch
    {
        ConfigFieldType.String or ConfigFieldType.MultiLine => typeof(TextBox),
        ConfigFieldType.Int => typeof(StackPanel),
        ConfigFieldType.Bool => typeof(CheckBox),
        ConfigFieldType.Select => typeof(ComboBox),
        ConfigFieldType.FilePath or ConfigFieldType.FolderPath => typeof(DockPanel),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No editor is expected for this field type."),
    };

    private static string Describe(Control control) =>
        control is TextBlock text ? $"{nameof(TextBlock)} '{text.Text}'" : control.GetType().Name;

    // The width the block's text would take untrimmed, in its own font.
    private static double FullWidth(TextBlock text)
    {
        using var layout = new TextLayout(
            text.Text,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight),
            text.FontSize,
            text.Foreground,
            letterSpacing: text.LetterSpacing);
        return layout.Width;
    }
}
