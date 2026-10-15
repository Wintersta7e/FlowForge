using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The window at its minimum size: empty, with a template loaded, and with a station's
/// settings open in the inspector.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class WindowSizeTests
{
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
        session.TemplateCards().Should().HaveCount(PipelineTemplateLibrary.Templates.Count);
        session.TemplateCardsOutsideCanvas(bringIntoView: true).Should().BeEmpty("every template card scrolls fully into the canvas pane");
    }

    [AvaloniaFact]
    public void Minimum_size_template_loaded()
    {
        using var session = VisualSession.Open(_output, MinimumSize);
        session.LoadTemplate(VisualSession.WebExportTemplate);
        using WriteableBitmap frame = session.Capture(VisualFrames.MinIdle);

        AssertMinimumSize(session, frame);
        int templateNodes = PipelineTemplateLibrary.CreateFromTemplate(VisualSession.WebExportTemplate).Nodes.Count;
        _output.WriteLine($"nodes={session.ViewModel.Editor.Nodes.Count} template={templateNodes}");
        session.ViewModel.Editor.Nodes.Should().HaveCount(templateNodes);
    }

    [AvaloniaFact]
    public void Minimum_size_with_inspector()
    {
        using var session = VisualSession.Open(_output, MinimumSize);
        EditorViewModel editor = session.ViewModel.Editor;
        session.LoadTemplate(VisualSession.WebExportTemplate);
        PipelineNodeViewModel station = editor.Nodes[0];

        session.Click(session.Stations().Single(c => ReferenceEquals(c.DataContext, station)));
        session.RenderUntil(
            () => editor.SelectedNode is not null && session.InspectorReady(),
            "a station selected and its fields shown");
        using WriteableBitmap frame = session.Capture(VisualFrames.MinInspector);

        AssertMinimumSize(session, frame);
        _output.WriteLine($"clicked={station.TypeKey} selected={editor.SelectedNode?.TypeKey} fields={session.ViewModel.Properties.Fields.Count}");
        editor.SelectedNode.Should().BeSameAs(station, "the click selects the station under the pointer");
    }

    private void AssertMinimumSize(VisualSession session, WriteableBitmap frame)
    {
        _output.WriteLine($"client={session.Window.ClientSize} frame={frame.PixelSize}");
        session.Window.ClientSize.Should().Be(MinimumSize);
        frame.PixelSize.Should().Be(new PixelSize(900, 560));
    }
}
