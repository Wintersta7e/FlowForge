using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The window at its minimum size: empty, with a template loaded, and with a station's
/// settings open in the inspector.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class WindowSizeTests
{
    private const string WebExportTemplate = "image-web-export";

    private static readonly Size MinimumSize = new(900, 560);

    private readonly ITestOutputHelper _output;

    public WindowSizeTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Minimum_size_empty_state()
    {
        using var session = VisualSession.Open(_output, MinimumSize);
        using WriteableBitmap frame = session.Capture(VisualFrames.MinEmpty);

        AssertMinimumSize(session, frame);
        session.ViewModel.Editor.Nodes.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Minimum_size_template_loaded()
    {
        using var session = VisualSession.Open(_output, MinimumSize);
        LoadTemplate(session);
        using WriteableBitmap frame = session.Capture(VisualFrames.MinIdle);

        AssertMinimumSize(session, frame);
        int templateNodes = PipelineTemplateLibrary.CreateFromTemplate(WebExportTemplate).Nodes.Count;
        _output.WriteLine($"nodes={session.ViewModel.Editor.Nodes.Count} template={templateNodes}");
        session.ViewModel.Editor.Nodes.Should().HaveCount(templateNodes);
    }

    [AvaloniaFact]
    public void Minimum_size_with_inspector()
    {
        using var session = VisualSession.Open(_output, MinimumSize);
        EditorViewModel editor = session.ViewModel.Editor;
        LoadTemplate(session);
        PipelineNodeViewModel station = editor.Nodes[0];

        Click(session.Window, CentreInWindow(Stations(session.Window).Single(c => ReferenceEquals(c.DataContext, station))));
        session.RenderUntil(
            () => editor.SelectedNode is not null && InspectorReady(session),
            "a station selected and its fields shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.MinInspector);

        AssertMinimumSize(session, frame);
        _output.WriteLine($"clicked={station.TypeKey} selected={editor.SelectedNode?.TypeKey} fields={session.ViewModel.Properties.Fields.Count}");
        editor.SelectedNode.Should().BeSameAs(station, "the click selects the station under the pointer");
    }

    private static void LoadTemplate(VisualSession session)
    {
        session.ViewModel.LoadTemplateCommand.Execute(WebExportTemplate);
        session.RenderUntil(() => Stations(session.Window).Count == session.ViewModel.Editor.Nodes.Count, "template stations laid out");
    }

    private static List<ItemContainer> Stations(Window window) =>
        window.GetVisualDescendants().OfType<ItemContainer>()
            .Where(c => c.DataContext is PipelineNodeViewModel && c.Bounds.Width > 0)
            .ToList();

    private static Point CentreInWindow(Avalonia.Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");

    private static void Click(TopLevel topLevel, Point point)
    {
        topLevel.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        topLevel.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    // Every current field has a laid-out card in the inspector.
    private static bool InspectorReady(VisualSession session)
    {
        var cards = session.Window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(list => ReferenceEquals(list.ItemsSource, session.ViewModel.Properties.Fields))
            .GetRealizedContainers().OfType<ContentPresenter>().ToList();
        return cards.Count > 0
            && cards.Select(card => card.DataContext).SequenceEqual(session.ViewModel.Properties.Fields)
            && cards.All(card => card.Child is { Bounds.Width: > 0 });
    }

    private void AssertMinimumSize(VisualSession session, WriteableBitmap frame)
    {
        _output.WriteLine($"client={session.Window.ClientSize} frame={frame.PixelSize}");
        session.Window.ClientSize.Should().Be(MinimumSize);
        frame.PixelSize.Should().Be(new PixelSize(900, 560));
    }
}
