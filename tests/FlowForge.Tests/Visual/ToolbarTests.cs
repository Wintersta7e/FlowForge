using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The toolbar: Undo and Redo enabled only while the history has a step to take, with a
/// tooltip naming that step, and drawn dimmed rather than filled while disabled; the open
/// file's folder and name with its full path on hover; and the build's version.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ToolbarTests
{
    private const int ColourTolerance = 8;
    private const string DocumentSlotId = "ToolbarDocumentPath";
    private const string HelpButtonName = "Keyboard Shortcuts";

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

    [AvaloniaFact]
    public void VersionLabel_ShowsInToolbar()
    {
        using var session = VisualSession.Open(_output);
        ToolbarView toolbar = session.Window.GetVisualDescendants().OfType<ToolbarView>().Single();
        string label = session.ViewModel.VersionLabel;

        var shown = toolbar.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && string.Equals(t.Text, label, StringComparison.Ordinal))
            .ToList();
        bool bound = shown.Count == 1 && BindingOperations.GetBindingExpressionBase(shown[0], TextBlock.TextProperty) is not null;
        _output.WriteLine($"label='{label}' matches={shown.Count} bound={bound} toolbar=[{string.Join(" | ", session.ShownText(toolbar))}]");

        shown.Should().ContainSingle("the toolbar shows the version label once");
        bound.Should().BeTrue("the label follows the build's version rather than a fixed number");
    }

    [AvaloniaFact]
    public void PathSlot_FitsAndShowsFullPath()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        Button help = session.ButtonNamed(HelpButtonName);
        Control slot = session.ById<Control>(DocumentSlotId);

        PrintSlot("untitled", session, slot);
        session.ShownText(slot).Should().Equal(["untitled.ffpipe"], "an untitled pipeline shows the default name and no folder");
        vm.DocumentFolder.Should().BeEmpty();

        string name = string.Concat(Enumerable.Repeat("long-file-name-", 8))[..113] + ".ffpipe";
        string path = Path.Combine(session.Temp.Path, name);
        vm.CurrentFilePath = path;
        session.RenderUntil(() => session.ShownText(slot).Contains(name, StringComparer.Ordinal), "the open file's name in the slot");

        PrintSlot($"open name-length={name.Length}", session, slot);
        session.ShownText(slot).Should().Equal($"/{Path.GetFileName(session.Temp.Path)}/", name);
        (ToolTip.GetTip(slot) as string).Should().Be(path, "hovering the slot shows the full path");
        session.InsideClient(HelpButtonName, help).Should().BeTrue("a long file name is trimmed, not pushing the toolbar off screen");
        TextBlock sourceCount = session.ById<TextBlock>("ToolbarSourceCount");
        sourceCount.IsEffectivelyVisible.Should().BeTrue("the live counts show at the default size");

        var minimum = new Size(session.Window.MinWidth, session.Window.MinHeight);
        session.Window.Width = minimum.Width;
        session.Window.Height = minimum.Height;
        session.RenderUntil(() => session.Window.ClientSize == minimum, "the window at its minimum size");

        PrintSlot($"open at {minimum}", session, slot);
        (ToolTip.GetTip(slot) as string).Should().Be(path);
        session.InsideClient(HelpButtonName, help).Should().BeTrue("the shortcuts button stays on screen at the window's minimum size");
        sourceCount.IsEffectivelyVisible.Should().BeFalse("the live counts give way to the run controls in a narrow window");
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

    private void PrintSlot(string state, VisualSession session, Control slot) =>
        _output.WriteLine(
            $"{state}: slot=[{string.Join(" | ", session.ShownText(slot))}] bounds={session.BoundsInWindow(slot)} " +
            $"tip='{ToolTip.GetTip(slot)}' folder='{session.ViewModel.DocumentFolder}' name='{session.ViewModel.DocumentName}'");
}
