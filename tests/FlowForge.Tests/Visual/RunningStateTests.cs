using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The running look without a real run: DEMO mode lights every station and pipe, and the
/// mercury beads travel along the pipes.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class RunningStateTests
{
    private readonly ITestOutputHelper _output;

    public RunningStateTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Demo_mode_runs_every_station_and_beads_reach_mid_flight()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        EditorViewModel editor = vm.Editor;
        MainWindow window = session.Window;

        // Fitting measures the laid-out stations, so they must exist first.
        session.LoadTemplate(VisualSession.WebExportTemplate);
        editor.RequestFitToScreen();
        NodifyEditor canvas = window.GetVisualDescendants().OfType<NodifyEditor>().Single(e => string.Equals(e.Name, "Editor", StringComparison.Ordinal));
        var visible = new Rect(canvas.Bounds.Size);

        // Both corners go through the zoom transform; a station's own bounds are unscaled.
        session.RenderUntil(
            () => session.Stations().All(s =>
                visible.Contains(s.TranslatePoint(default, canvas)!.Value)
                && visible.Contains(s.TranslatePoint(new Point(s.Bounds.Width, s.Bounds.Height), canvas)!.Value)),
            "every template station fitted on screen");

        vm.IsDemoMode = true;
        try
        {
            session.RenderUntil(
                () => VisibleDroplets(window) is { Count: > 0 } droplets && 2 * droplets.Count(InFlight) >= droplets.Count,
                "beads mid-flight");
            using WriteableBitmap frame = session.Capture(VisualFrames.RunDemo);

            List<MwMercuryDroplet> shown = VisibleDroplets(window);
            var auras = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("mw-aura")).ToList();
            var beadCanvases = window.GetVisualDescendants().OfType<MwMercuryDroplet>()
                .Select(d => d.Parent).OfType<Canvas>().Distinct().ToList();
            _output.WriteLine($"droplets visible={shown.Count} mid-flight={shown.Count(InFlight)}");
            _output.WriteLine($"stations={editor.Nodes.Count} running={editor.Nodes.Count(n => n.IsRunning)} auras visible={auras.Count(a => a.IsEffectivelyVisible)}/{auras.Count}");
            _output.WriteLine($"pipes={editor.Connections.Count} running={editor.Connections.Count(c => c.IsRunning)} bead canvases visible={beadCanvases.Count(c => c.IsEffectivelyVisible)}/{beadCanvases.Count}");
            editor.Connections.Should().NotBeEmpty("the template connects its stations");
            editor.Nodes.Should().OnlyContain(n => n.IsRunning, "DEMO mode lights every station");
            editor.Connections.Should().OnlyContain(c => c.IsRunning, "DEMO mode lights every pipe");
            auras.Should().HaveCount(editor.Nodes.Count).And.OnlyContain(a => a.IsEffectivelyVisible);
            beadCanvases.Should().HaveCount(editor.Connections.Count).And.OnlyContain(c => c.IsEffectivelyVisible);
        }
        finally
        {
            // The beads animate for as long as DEMO mode is on; stop it before the window closes.
            vm.IsDemoMode = false;
        }
    }

    private static bool InFlight(MwMercuryDroplet droplet) => droplet.Progress > 0.05 && droplet.Progress < 0.95;

    private static List<MwMercuryDroplet> VisibleDroplets(Window window) =>
        window.GetVisualDescendants().OfType<MwMercuryDroplet>().Where(d => d.IsEffectivelyVisible).ToList();
}
