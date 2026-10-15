using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FlowForge.Core.Pipeline;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The toolbar's Undo and Redo buttons: enabled only while the history has a step to take,
/// with a tooltip naming that step.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ToolbarTests
{
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

    private void PrintState(string state, Button undo, Button redo) =>
        _output.WriteLine(
            $"{state}: undo enabled={undo.IsEffectivelyEnabled} tip='{ToolTip.GetTip(undo)}' " +
            $"redo enabled={redo.IsEffectivelyEnabled} tip='{ToolTip.GetTip(redo)}'");
}
