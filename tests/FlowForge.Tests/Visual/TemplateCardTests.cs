using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline.Templates;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The empty canvas's template cards: one per library template, each showing that template's
/// title, description and station count, inside the canvas pane, and loading it when clicked.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class TemplateCardTests
{
    private readonly ITestOutputHelper _output;

    public TemplateCardTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void TemplateCards_MatchLibraryAndLoadOnClick()
    {
        using var session = VisualSession.Open(_output);
        IReadOnlyList<PipelineTemplate> templates = PipelineTemplateLibrary.Templates;
        IReadOnlyList<TemplateCardViewModel> cards = session.ViewModel.Editor.TemplateCards;
        session.RenderUntil(() => session.TemplateCards().Count == templates.Count, "template cards laid out");
        List<Button> shown = session.TemplateCards();

        _output.WriteLine($"cards={cards.Count} templates={templates.Count} shown={shown.Count}");
        cards.Should().HaveCount(templates.Count);
        shown.Select(button => button.Tag).Should().Equal(templates.Select(template => template.Id));
        for (int i = 0; i < templates.Count; i++)
        {
            PipelineTemplate template = templates[i];
            int stations = template.Graph.Nodes.Count;
            List<string> text = session.ShownText(shown[i]);
            _output.WriteLine($"{template.Id}: stations={cards[i].StationCount} graph={stations} shown=[{string.Join(" | ", text)}]");
            cards[i].Id.Should().Be(template.Id);
            cards[i].StationCount.Should().Be(stations);
            text.Should().Contain([$"TPL.{i + 1:00}", template.ShortName, template.Description, $"{stations} stations", "ignite ▸"]);
        }

        PipelineTemplate first = templates[0];
        session.Click(shown[0]);
        session.RenderUntil(() => session.ViewModel.Editor.Nodes.Count == first.Graph.Nodes.Count, $"{first.Id} loaded by its card");
        _output.WriteLine($"clicked={first.Id} nodes={session.ViewModel.Editor.Nodes.Count}");
    }

    // At the default size every card shows without scrolling; at the minimum size the empty state scrolls.
    [AvaloniaTheory]
    [InlineData(1400, 820, false)]
    [InlineData(900, 560, true)]
    public void TemplateCards_FitCanvasPane(int width, int height, bool bringIntoView)
    {
        using var session = VisualSession.Open(_output, new Size(width, height));
        int templates = PipelineTemplateLibrary.Templates.Count;
        session.RenderUntil(() => session.TemplateCards().Count == templates, "template cards laid out");

        session.TemplateCardsOutsideCanvas(bringIntoView).Should().BeEmpty("every template card lies inside the canvas pane");
    }

    // The canvas hint sits behind the empty state's text; it shows only once there are stations to drag.
    [AvaloniaTheory]
    [InlineData(1400, 820)]
    [InlineData(900, 560)]
    public void Canvas_hint_shows_only_once_the_canvas_has_stations(int width, int height)
    {
        using var session = VisualSession.Open(_output, new Size(width, height));
        session.RenderUntil(() => session.TemplateCards().Count == PipelineTemplateLibrary.Templates.Count, "template cards laid out");
        TextBlock hint = session.ById<TextBlock>("CanvasHint");
        _output.WriteLine($"empty: hint visible={hint.IsEffectivelyVisible}");
        hint.IsEffectivelyVisible.Should().BeFalse("the hint does not show through the empty state");

        session.LoadTemplate(VisualSession.WebExportTemplate);
        PixelRect pane = session.BoundsInWindow(session.Window.GetVisualDescendants().OfType<CanvasView>().Single());
        _output.WriteLine($"loaded: nodes={session.ViewModel.Editor.Nodes.Count} hint visible={hint.IsEffectivelyVisible} hint={session.BoundsInWindow(hint)} pane={pane}");
        hint.IsEffectivelyVisible.Should().BeTrue("the hint shows once the canvas has stations");
    }

    // The empty state's scrolling must not take the wheel from the editor once a pipeline is open.
    [AvaloniaFact]
    public void Wheel_zooms_the_editor_once_a_template_is_loaded()
    {
        using var session = VisualSession.Open(_output);
        session.LoadTemplate(VisualSession.WebExportTemplate);
        NodifyEditor canvas = session.Window.GetVisualDescendants().OfType<NodifyEditor>()
            .Single(editor => string.Equals(editor.Name, "Editor", StringComparison.Ordinal));
        double before = canvas.ViewportZoom;

        session.Window.MouseWheel(session.CentreOf(canvas), new Vector(0, 1), RawInputModifiers.None);
        session.RenderUntil(() => canvas.ViewportZoom != before, "the wheel zooms the editor");
        _output.WriteLine($"zoom before={before} after={canvas.ViewportZoom}");
    }
}
