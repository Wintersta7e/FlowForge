using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The toolbar's Undo and Redo buttons: enabled only while the history has a step to take,
/// with a tooltip naming that step, and drawn dimmed rather than filled while disabled.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ToolbarTests
{
    private const int ColourTolerance = 8;

    private readonly ITestOutputHelper _output;

    public ToolbarTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void UndoRedo_FollowHistory()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        Button undo = session.ButtonNamed("Undo");
        Button redo = session.ButtonNamed("Redo");

        PrintState("fresh", undo, redo);
        undo.IsEffectivelyEnabled.Should().BeFalse("a fresh session has nothing to undo");
        redo.IsEffectivelyEnabled.Should().BeFalse("a fresh session has nothing to redo");

        var definition = new NodeDefinition { TypeKey = "RenamePattern", Position = new CanvasPosition(120, 80) };
        var add = new AddNodeCommand(editor.Nodes, new PipelineNodeViewModel(definition, session.ViewModel.Registry));
        editor.UndoRedo.Execute(add);
        session.RenderUntil(() => undo.IsEffectivelyEnabled, "Undo enabled after an edit");
        PrintState("edited", undo, redo);
        (ToolTip.GetTip(undo) as string).Should().Contain(add.Description);
        redo.IsEffectivelyEnabled.Should().BeFalse("nothing has been undone");

        session.Click(undo);
        session.RenderUntil(() => redo.IsEffectivelyEnabled, "Redo enabled after Undo");
        PrintState("undone", undo, redo);
        editor.Nodes.Should().BeEmpty();
        (ToolTip.GetTip(redo) as string).Should().Contain(add.Description);
        undo.IsEffectivelyEnabled.Should().BeFalse("the only edit has been undone");
    }

    [AvaloniaFact]
    public void DisabledUndo_LooksIdleWithADimmedIcon()
    {
        using var session = VisualSession.Open(_output);
        Button undo = session.ButtonNamed("Undo");
        Button save = session.ButtonNamed("Save");
        session.RenderUntil(() => !undo.IsEffectivelyEnabled && save.IsEffectivelyEnabled, "Undo disabled, Save enabled");
        using WriteableBitmap frame = session.Capture(VisualFrames.ToolbarUndoDisabled);

        (PixelRect undoBand, Color undoBackground, Color undoIcon) = Paint(session, frame, undo);
        (PixelRect saveBand, Color saveBackground, Color saveIcon) = Paint(session, frame, save);
        _output.WriteLine($"tolerance={ColourTolerance} undo band={undoBand} background={undoBackground} icon-brightest={undoIcon}");
        _output.WriteLine($"save band={saveBand} background={saveBackground} icon-brightest={saveIcon}");

        WithinTolerance(undoBackground, saveBackground).Should().BeTrue("a disabled toolbar button keeps the idle button's background");
        Brightness(undoIcon).Should().BeLessThan(Brightness(saveIcon), "a disabled toolbar button dims its icon");
    }

    // The left padding of a toolbar button, clear of its icon, and the brightest pixel of its icon.
    private static (PixelRect Band, Color Background, Color Icon) Paint(VisualSession session, WriteableBitmap frame, Button button)
    {
        PixelRect bounds = session.BoundsInWindow(button);
        var band = new PixelRect(bounds.X + 2, bounds.Y + 4, 4, bounds.Height - 8);
        PathIcon icon = button.GetVisualDescendants().OfType<PathIcon>().Single();
        Color brightest = session.Pixels(frame, session.BoundsInWindow(icon)).MaxBy(Brightness);
        return (band, session.MeanColour(frame, band), brightest);
    }

    private static int Brightness(Color colour) => colour.R + colour.G + colour.B;

    private static bool WithinTolerance(Color actual, Color expected) =>
        Math.Abs(actual.R - expected.R) <= ColourTolerance
        && Math.Abs(actual.G - expected.G) <= ColourTolerance
        && Math.Abs(actual.B - expected.B) <= ColourTolerance;

    private void PrintState(string state, Button undo, Button redo) =>
        _output.WriteLine(
            $"{state}: undo enabled={undo.IsEffectivelyEnabled} tip='{ToolTip.GetTip(undo)}' " +
            $"redo enabled={redo.IsEffectivelyEnabled} tip='{ToolTip.GetTip(redo)}'");
}
