using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
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
        NodifyEditor editor = Editor(window);
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
        Connector aOut = WaitForPort(session, a.Output[0]);
        Connector aIn = WaitForPort(session, a.Input[0]);
        Connector bOut = WaitForPort(session, b.Output[0]);
        Connector bIn = WaitForPort(session, b.Input[0]);

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

    [AvaloniaFact]
    public async Task Edited_fields_survive_save_and_reload()
    {
        using var session = VisualSession.Open(_output);
        MainWindow window = session.Window;
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("RenamePattern", new Point(80, 120), session.ViewModel.Registry);
        editor.AddNode("FolderOutput", new Point(440, 120), session.ViewModel.Registry);
        PipelineNodeViewModel rename = editor.Nodes[0];
        PipelineNodeViewModel output = editor.Nodes[1];

        Select(session, rename);
        EditField(session, "pattern", () =>
        {
            session.Click(FieldEditor<TextBox>(session, "pattern"));
            window.KeyTextInput(EditedPattern);
        });
        EditField(session, "startIndex", () =>
        {
            session.Click(FieldEditor<TextBox>(session, "startIndex"));
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.A, "a");
            window.KeyTextInput("7");
        });
        Select(session, output);
        EditField(session, "overwrite", () => session.Click(FieldEditor<CheckBox>(session, "overwrite")));
        EditField(session, "mode", () => PickOption(session, FieldEditor<ComboBox>(session, "mode"), "move"));
        using WriteableBitmap frame = session.Capture(VisualFrames.EditPersists);

        (PipelineNodeViewModel Node, string Key, string Expected)[] edits =
        [
            (rename, "pattern", EditedPattern),
            (rename, "startIndex", "7"),
            (output, "overwrite", "true"),
            (output, "mode", "move"),
        ];
        foreach ((PipelineNodeViewModel node, string key, string expected) in edits)
        {
            ValueText(node.Config, key).Should().Be(expected, $"typing into {key} updates the node's config");
        }

        string path = Path.Combine(session.Temp.Path, "edit.ffpipe");
        await PipelineSerializer.SaveAsync(editor.BuildGraph(), path);
        PipelineGraph reloaded = await PipelineSerializer.LoadAsync(path);
        foreach ((PipelineNodeViewModel node, string key, string expected) in edits)
        {
            string? value = ValueText(reloaded.Nodes.Single(n => n.Id == node.Id).Config, key);
            _output.WriteLine($"reloaded {node.TypeKey}.{key}={value}");
            value.Should().Be(expected, $"{key} survives a save and reload");
        }
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
        NodifyEditor canvas = Editor(window);
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
        NodifyEditor canvas = Editor(session.Window);
        var visible = new Rect(canvas.Bounds.Size);

        // Both corners go through the zoom transform; a station's own bounds are unscaled.
        session.RenderUntil(
            () => session.Stations().All(s =>
                visible.Contains(s.TranslatePoint(default, canvas)!.Value)
                && visible.Contains(s.TranslatePoint(new Point(s.Bounds.Width, s.Bounds.Height), canvas)!.Value)),
            "every template station fitted on screen");
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

    private static NodifyEditor Editor(Window window) =>
        window.GetVisualDescendants().OfType<NodifyEditor>().Single(e => string.Equals(e.Name, "Editor", StringComparison.Ordinal));

    private static IEnumerable<TreeViewItem> LibraryRows(Window window) =>
        window.GetVisualDescendants().OfType<TreeViewItem>().Where(item => item.DataContext is NodeLibraryItemViewModel);

    private static Point InWindow(Avalonia.Visual visual, Point point) =>
        visual.TranslatePoint(point, TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");

    private static Connector WaitForPort(VisualSession session, PipelineConnectorViewModel port)
    {
        Connector? Find() => session.Window.GetVisualDescendants().OfType<Connector>()
            .SingleOrDefault(c => ReferenceEquals(c.DataContext, port) && c.Bounds.Width > 0);
        session.RenderUntil(() => Find() is not null, $"{port.Node.TypeKey} {(port.IsInput ? "input" : "output")} port shown");
        return Find()!;
    }

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

    // Each config edit rebuilds the inspector's fields; wait for the rebuilt cards before the next gesture.
    private static void EditField(VisualSession session, string key, Action input)
    {
        ConfigFieldViewModel before = session.ViewModel.Properties.Fields[0];
        input();
        session.RenderUntil(
            () => session.ViewModel.Properties.Fields.Count > 0
                && !ReferenceEquals(session.ViewModel.Properties.Fields[0], before)
                && session.InspectorReady(),
            $"inspector rebuilt after editing {key}");
    }

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
        ConfigFieldType.String or ConfigFieldType.Int or ConfigFieldType.MultiLine => typeof(TextBox),
        ConfigFieldType.Bool => typeof(CheckBox),
        ConfigFieldType.Select => typeof(ComboBox),
        ConfigFieldType.FilePath or ConfigFieldType.FolderPath => typeof(DockPanel),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No editor is expected for this field type."),
    };

    private static string Describe(Control control) =>
        control is TextBlock text ? $"{nameof(TextBlock)} '{text.Text}'" : control.GetType().Name;
}
