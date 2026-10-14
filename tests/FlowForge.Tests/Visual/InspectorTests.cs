using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// A station answers the pointer as the editor draws it: a hovered station lifts.
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
}
