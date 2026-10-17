using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// Alt+click on a port, the disconnect gesture the docs name, driven by pointer input against
/// the real window.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class DisconnectTests
{
    private readonly ITestOutputHelper _output;

    public DisconnectTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void AltClickOnPort_DisconnectsAllAndUndoRestores()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;

        // The source feeds two stations and one of them feeds an output, so the click must take
        // the source's two pipes and leave the third.
        NodeDefinition source = At(PipelineBuilder.Node("FolderInput", new { path = session.Temp.Path }), 40, 160);
        NodeDefinition upper = At(PipelineBuilder.Node("RenameAddAffix", new { prefix = "a_" }), 360, 40);
        NodeDefinition lower = At(PipelineBuilder.Node("RenameAddAffix", new { prefix = "b_" }), 360, 280);
        NodeDefinition output = At(PipelineBuilder.Node("FolderOutput", new { path = session.Temp.OutputPath, mode = "copy" }), 680, 40);
        var graph = new PipelineGraph { Name = "two pipes on one port", Nodes = [source, upper, lower, output] };
        graph.Connections.Add(new Connection { FromNode = source.Id, ToNode = upper.Id });
        graph.Connections.Add(new Connection { FromNode = source.Id, ToNode = lower.Id });
        graph.Connections.Add(new Connection { FromNode = upper.Id, ToNode = output.Id });
        editor.LoadGraph(graph, session.ViewModel.Registry).Should().Be(0, "the editor keeps every connection");

        PipelineConnectorViewModel port = editor.Nodes.Single(n => n.Id == source.Id).Output[0];
        int k = editor.Connections.Count(c => c.Source == port);
        int before = editor.Connections.Count;
        Point centre = session.CentreOf(session.WaitForPort(port));

        // A real pointer reaches the port before pressing; a press that is the editor's first
        // pointer event is one no mouse sends, and the editor mishandles its release.
        session.Window.MouseMove(centre, RawInputModifiers.None);
        session.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.Alt);
        session.Window.MouseUp(centre, MouseButton.Left, RawInputModifiers.Alt);
        session.Pump();
        int afterClick = editor.Connections.Count;
        List<string> wrongAfterClick = PortsDisagreeing(editor);
        int undoSteps = editor.UndoRedo.CanUndo ? 1 : 0;

        editor.Undo();
        session.Pump();
        int afterUndo = editor.Connections.Count;
        List<string> wrongAfterUndo = PortsDisagreeing(editor);

        _output.WriteLine($"k={k} connections before={before} after alt-click={afterClick} after one undo={afterUndo} undo available after click={undoSteps}");
        _output.WriteLine($"ports whose IsConnected disagrees with the pipes: after click=[{string.Join(", ", wrongAfterClick)}] after undo=[{string.Join(", ", wrongAfterUndo)}]");
        k.Should().Be(2, "the source's output feeds two stations");
        afterClick.Should().Be(before - k, "Alt+click on a port removes every pipe on it");
        wrongAfterClick.Should().BeEmpty("each port shows connected exactly when a pipe remains on it");
        afterUndo.Should().Be(before, "one Undo restores every pipe the click removed");
        wrongAfterUndo.Should().BeEmpty("each port shows connected again once its pipes are back");
    }

    private static NodeDefinition At(NodeDefinition node, double x, double y)
    {
        node.Position = new CanvasPosition(x, y);
        return node;
    }

    private static List<string> PortsDisagreeing(EditorViewModel editor) =>
        editor.Nodes
            .SelectMany(node => node.Input.Concat(node.Output))
            .Where(p => p.IsConnected != editor.Connections.Any(c => c.Source == p || c.Target == p))
            .Select(p => $"{p.Node.TypeKey} {(p.IsInput ? "in" : "out")} IsConnected={p.IsConnected}")
            .ToList();
}
