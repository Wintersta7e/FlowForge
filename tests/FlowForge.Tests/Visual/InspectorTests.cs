using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// A station answers the pointer as the editor draws it: a hovered station lifts. The
/// inspector's field editors show true watermarks and stay inside their cards, and a station's
/// preview follows an edit made there and its undo.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class InspectorTests
{
    private readonly ITestOutputHelper _output;

    public InspectorTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void StationHover_LiftsAndRestores()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("FolderInput", new Point(200, 160), session.ViewModel.Registry);
        ItemContainer station = session.WaitForStation(editor.Nodes.Single());

        // The station's own root, as its template draws it inside the editor's container.
        Control root = station.GetVisualDescendants().OfType<ContentPresenter>()
            .Select(presenter => presenter.Child)
            .OfType<Control>()
            .First(child => child.DataContext is PipelineNodeViewModel);
        Point centre = session.CentreOf(root);

        session.Window.MouseMove(centre, RawInputModifiers.None);
        session.RenderUntil(() => root.IsPointerOver, "pointer over the station");
        Matrix? hovered = root.RenderTransform?.Value;

        // Straight down, past the station's bottom edge.
        session.Window.MouseMove(centre + new Vector(0, root.Bounds.Height), RawInputModifiers.None);
        session.RenderUntil(() => !root.IsPointerOver, "pointer off the station");
        Matrix? left = root.RenderTransform?.Value;

        _output.WriteLine($"root={root.GetType().Name} classes=[{string.Join(", ", root.Classes)}] centre={centre}");
        _output.WriteLine($"hovered transform={hovered?.ToString() ?? "none"} left transform={left?.ToString() ?? "none"}");
        hovered.Should().Be(Matrix.CreateTranslation(0, -1), "a hovered station lifts by one pixel");
        (left is null || left == Matrix.Identity).Should().BeTrue("a station the pointer has left sits back in place");
    }

    [AvaloniaFact]
    public void InspectorFields_ShowTrueWatermarksAndContainedEditors()
    {
        using var session = VisualSession.Open(_output);
        NodeRegistry registry = session.ViewModel.Registry;
        var typeKeys = registry.GetRegisteredTypeKeys().Order(StringComparer.Ordinal).ToList();
        var scanned = new List<ConfigFieldType>();
        var misses = new List<string>();
        foreach (string typeKey in typeKeys)
        {
            foreach (ContentPresenter card in ShowInspector(session, typeKey))
            {
                var field = (ConfigFieldViewModel)card.DataContext!;
                scanned.Add(field.FieldType);
                if (FieldMiss(typeKey, field, card) is { } miss)
                {
                    misses.Add(miss);
                }
            }
        }

        int intFields = scanned.Count(type => type == ConfigFieldType.Int);
        int boolFields = scanned.Count(type => type == ConfigFieldType.Bool);

        (Control multiLineCard, TextBox editor) = ShowLongMultiLineValue(session, typeKeys);
        PixelRect cardBounds = session.BoundsInWindow(multiLineCard);
        PixelRect editorBounds = session.BoundsInWindow(editor);
        int lines = editor.GetVisualDescendants().OfType<TextPresenter>().Single().TextLayout.TextLines.Count;
        PixelRect inspectorBounds = session.BoundsInWindow(multiLineCard.GetVisualAncestors().OfType<ScrollViewer>().First());

        _output.WriteLine($"scanned types={typeKeys.Count} int-fields={intFields} bool-fields={boolFields} misses={misses.Count}");
        _output.WriteLine($"multi-line: chars={editor.Text?.Length} lines={lines} editor={editorBounds} card={cardBounds} inspector={inspectorBounds}");
        intFields.Should().BePositive();
        boolFields.Should().BePositive();
        misses.Should().BeEmpty();
        lines.Should().BeGreaterThan(1, "a long multi-line value wraps");
        cardBounds.Contains(editorBounds).Should().BeTrue("the multi-line editor stays inside its field card");
        inspectorBounds.Contains(cardBounds).Should().BeTrue("the field card stays inside the inspector");
    }

    [AvaloniaFact]
    public void StationPreview_FollowsEditAndUndo()
    {
        const string original = "{name}_before{ext}";
        const string edited = "{name}_after{ext}";
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        var definition = new NodeDefinition
        {
            TypeKey = "RenamePattern",
            Position = new CanvasPosition(200, 160),
            Config = ConfigHelper.MakeConfig(("pattern", original)),
        };
        var node = new PipelineNodeViewModel(definition, session.ViewModel.Registry);
        editor.UndoRedo.Execute(new AddNodeCommand(editor.Nodes, node));
        ItemContainer station = session.WaitForStation(node);
        TextBlock preview = station.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => string.Equals(text.Text, original, StringComparison.Ordinal));
        session.Click(station);
        session.RenderUntil(
            () => ReferenceEquals(editor.SelectedNode, node) && session.InspectorReady(),
            "RenamePattern selected and its fields shown");

        session.ViewModel.Properties.Fields.Single(f => string.Equals(f.Key, "pattern", StringComparison.Ordinal)).Value = edited;
        session.Pump();
        string? afterEdit = preview.Text;
        session.ViewModel.UndoCommand.Execute(null);
        session.Pump();
        string? afterUndo = preview.Text;

        _output.WriteLine($"preview before='{original}' after edit='{afterEdit}' after undo='{afterUndo}' shown={preview.IsEffectivelyVisible}");
        afterEdit.Should().Be(edited, "the station's preview follows an inspector edit");
        afterUndo.Should().Be(original, "Undo restores the station's preview");
    }

    // Shows one fresh node of this type in the inspector, selected by a click on its station.
    private static List<ContentPresenter> ShowInspector(VisualSession session, string typeKey)
    {
        EditorViewModel editor = session.ViewModel.Editor;
        editor.ClearAll();
        editor.AddNode(typeKey, new Point(200, 160), session.ViewModel.Registry);
        PipelineNodeViewModel node = editor.Nodes.Single();
        session.Click(session.WaitForStation(node));
        session.RenderUntil(
            () => ReferenceEquals(editor.SelectedNode, node) && session.InspectorReady(),
            $"{typeKey} selected and its fields shown");
        return session.InspectorCards();
    }

    private static (Control Card, TextBox Editor) MultiLineCard(List<ContentPresenter> cards, string key)
    {
        ContentPresenter card = cards.Single(c => string.Equals(((ConfigFieldViewModel)c.DataContext!).Key, key, StringComparison.Ordinal));
        return (card.Child!, card.GetVisualDescendants().OfType<TextBox>().Single());
    }

    // The first node with a multi-line field, that field holding 500 characters with no space to break at.
    private (Control Card, TextBox Editor) ShowLongMultiLineValue(VisualSession session, List<string> typeKeys)
    {
        NodeRegistry registry = session.ViewModel.Registry;
        string typeKey = typeKeys.First(type => registry.GetConfigSchema(type).Any(f => f.Type == ConfigFieldType.MultiLine));
        string key = registry.GetConfigSchema(typeKey).First(f => f.Type == ConfigFieldType.MultiLine).Key;
        string text = new('w', 500);
        (Control before, TextBox box) = MultiLineCard(ShowInspector(session, typeKey), key);
        _output.WriteLine($"multi-line {typeKey}.{key} before: card={session.BoundsInWindow(before)} editor={session.BoundsInWindow(box)}");
        box.Text = text;

        // The edit commits and posts an inspector refresh; wait for the rebuilt card showing the value.
        session.Pump();
        session.RenderUntil(
            () => session.InspectorReady() && string.Equals(MultiLineCard(session.InspectorCards(), key).Editor.Text, text, StringComparison.Ordinal),
            "multi-line field shows the long value");
        return MultiLineCard(session.InspectorCards(), key);
    }

    // Prints an Int editor's watermark or a Bool editor's check box content; returns how it misses the rule, if it does.
    private string? FieldMiss(string typeKey, ConfigFieldViewModel field, ContentPresenter card)
    {
        if (field.FieldType == ConfigFieldType.Int)
        {
            string? watermark = card.GetVisualDescendants().OfType<TextBox>().Single().Watermark;
            string expected = field.Placeholder ?? field.DefaultValue ?? string.Empty;
            _output.WriteLine($"int {typeKey}.{field.Key}: watermark='{watermark}' expected='{expected}'");
            return string.Equals(watermark, expected, StringComparison.Ordinal)
                ? null
                : $"{typeKey}.{field.Key} watermark '{watermark}', expected '{expected}'";
        }

        if (field.FieldType == ConfigFieldType.Bool)
        {
            object? content = card.GetVisualDescendants().OfType<CheckBox>().Single().Content;
            _output.WriteLine($"bool {typeKey}.{field.Key}: content='{content}'");
            return content is null ? null : $"{typeKey}.{field.Key} check box content '{content}', expected none";
        }

        return null;
    }
}
