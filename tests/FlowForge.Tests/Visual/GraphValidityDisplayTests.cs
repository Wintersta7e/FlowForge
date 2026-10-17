using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// While editing, the stations and pipes the graph check names carry a flag whose tooltip is the
/// check's own message, and a flag clears once the graph no longer earns it.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class GraphValidityDisplayTests
{
    private const string BadgeClass = "mw-invalid-badge";
    private const string FlagClass = "mw-invalid-flag";
    private readonly ITestOutputHelper _output;

    public GraphValidityDisplayTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void ValidityFlags_MarkExactlyTheValidatorsNodes()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        NodeRegistry registry = session.ViewModel.Registry;
        editor.AddNode("FolderInput", new Point(60, 120), registry);
        editor.AddNode("RenamePattern", new Point(320, 320), registry);
        editor.AddNode("FolderOutput", new Point(580, 120), registry);
        PipelineNodeViewModel input = editor.Nodes[0];
        PipelineNodeViewModel transform = editor.Nodes[1];
        PipelineNodeViewModel output = editor.Nodes[2];
        Connect(editor, input, output);

        IReadOnlyList<GraphIssue> issues = PipelineGraphValidator.Validate(editor.BuildGraph(), registry).Issues;
        var named = issues.Where(i => i.NodeId is not null).Select(i => i.NodeId!.Value).ToHashSet();
        var flagged = editor.Nodes.Where(n => n.ValidationMessage is not null).Select(n => n.Id).ToHashSet();
        TextBlock badge = Badge(session, transform);
        session.Pump();
        using WriteableBitmap frame = session.Capture(VisualFrames.StationFlaggedUnconnected);
        _output.WriteLine($"flagged/total={flagged.Count}/{editor.Nodes.Count} named={named.Count} badge shown={badge.IsEffectivelyVisible} tip='{ToolTip.GetTip(badge)}'");

        named.Should().BeEquivalentTo(new[] { transform.Id }, "only the unconnected transform is off every path");
        flagged.Should().BeEquivalentTo(named, "the editor flags exactly the nodes the graph check names");
        transform.ValidationMessage.Should().Be(issues.Single(i => i.NodeId == transform.Id).Message);
        badge.IsEffectivelyVisible.Should().BeTrue("a flagged station shows its badge");
        ToolTip.GetTip(badge).Should().Be(transform.ValidationMessage);
        Badge(session, input).IsEffectivelyVisible.Should().BeFalse("an unflagged station shows no badge");
        Badge(session, output).IsEffectivelyVisible.Should().BeFalse("an unflagged station shows no badge");

        Connect(editor, input, transform);
        Connect(editor, transform, output);
        session.Pump();
        _output.WriteLine($"connected: message='{transform.ValidationMessage}' badge shown={badge.IsEffectivelyVisible}");
        transform.ValidationMessage.Should().BeNull("a transform on a path from the source to the output is not flagged");
        badge.IsEffectivelyVisible.Should().BeFalse("a cleared flag hides the badge");
    }

    [AvaloniaFact]
    public void LoadedDuplicateConnection_IsFlagged()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        NodeRegistry registry = session.ViewModel.Registry;
        PipelineGraph graph = PipelineBuilder.New("Duplicate")
            .AddSource("FolderInput", new { path = Path.Combine(session.Temp.Path, "in") })
            .AddOutput("FolderOutput", new { path = Path.Combine(session.Temp.Path, "out") })
            .Build();
        graph.Nodes[0].Position = new CanvasPosition(120, 120);
        graph.Nodes[1].Position = new CanvasPosition(560, 120);
        Connection first = graph.Connections.Single();
        graph.Connections.Add(new Connection { FromNode = first.FromNode, FromPin = first.FromPin, ToNode = first.ToNode, ToPin = first.ToPin });
        string expected = PipelineGraphValidator.Validate(graph, registry).Issues
            .Single(i => i.Kind == GraphIssueKind.DuplicateConnection).Message;

        editor.LoadGraph(graph, registry);
        session.RenderUntil(() => editor.Connections.All(c => Flags(session, c).Count == 1), "both pipes drawn");
        using WriteableBitmap frame = session.Capture(VisualFrames.PipeFlaggedDuplicate);
        var flags = editor.Connections.Select(c => Flags(session, c).Single()).ToList();
        foreach (PipelineConnectionViewModel connection in editor.Connections)
        {
            MwPipeConnection flag = Flags(session, connection).Single();
            _output.WriteLine($"pipe message='{connection.ValidationMessage}' flag shown={flag.IsEffectivelyVisible} tip='{ToolTip.GetTip(flag)}'");
        }

        editor.Connections.Should().HaveCount(2);
        editor.Connections.Should().AllSatisfy(c => c.ValidationMessage.Should().Be(expected));
        flags.Should().AllSatisfy(flag =>
        {
            flag.IsEffectivelyVisible.Should().BeTrue("a flagged pipe shows its dashed overlay");
            ToolTip.GetTip(flag).Should().Be(expected);
        });
    }

    private static void Connect(EditorViewModel editor, PipelineNodeViewModel from, PipelineNodeViewModel to) =>
        editor.UndoRedo.Execute(new ConnectCommand(editor.Connections, new PipelineConnectionViewModel(from.Output[0], to.Input[0])));

    private static TextBlock Badge(VisualSession session, PipelineNodeViewModel node) =>
        session.WaitForStation(node).GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Classes.Contains(BadgeClass));

    // The dashed overlay the pipe template draws over this connection.
    private static List<MwPipeConnection> Flags(VisualSession session, PipelineConnectionViewModel connection) =>
        session.Window.GetVisualDescendants().OfType<MwPipeConnection>()
            .Where(pipe => pipe.Classes.Contains(FlagClass) && ReferenceEquals(pipe.DataContext, connection))
            .ToList();
}
