using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class MainWindowViewModelTests
{
    /// <summary>
    /// The toolbar's version label names the major version the build carries, so a
    /// version bump cannot leave an old number on screen.
    /// </summary>
    [Fact]
    public void VersionLabel_MatchesVersionMajor()
    {
        MainWindowViewModel vm = BuildVm(out _, out _);
        string version = XDocument.Load(RepoRoot.Combine("Directory.Build.props")).Descendants("Version").Single().Value;
        string major = version.Split('.')[0];

        vm.VersionLabel.Should().Be("MOLTEN WORKS · V" + major);
    }

    /// <summary>
    /// Toggling ExecutionLog.IsRunning must flip every station + pipe on the
    /// canvas into the "forge lit" visual state and back.
    /// </summary>
    [Fact]
    public void ExecutionLog_IsRunning_propagates_to_nodes_and_connections()
    {
        MainWindowViewModel vm = BuildVm(out PipelineNodeViewModel nodeA, out PipelineNodeViewModel nodeB);
        PipelineConnectionViewModel connection = AddConnection(vm, nodeA, nodeB);

        vm.ExecutionLog.IsRunning = true;

        nodeA.IsRunning.Should().BeTrue();
        nodeB.IsRunning.Should().BeTrue();
        connection.IsRunning.Should().BeTrue();

        vm.ExecutionLog.IsRunning = false;

        nodeA.IsRunning.Should().BeFalse();
        nodeB.IsRunning.Should().BeFalse();
        connection.IsRunning.Should().BeFalse();
    }

    /// <summary>
    /// IsDemoMode must OR with ExecutionLog.IsRunning so the DEMO toolbar
    /// button can exercise the running visual without a real pipeline.
    /// </summary>
    [Fact]
    public void IsDemoMode_ORs_with_execution_running_for_visual_state()
    {
        MainWindowViewModel vm = BuildVm(out PipelineNodeViewModel nodeA, out _);

        vm.IsDemoMode = true;
        nodeA.IsRunning.Should().BeTrue();

        // Turning demo off while nothing is running returns to idle.
        vm.IsDemoMode = false;
        nodeA.IsRunning.Should().BeFalse();

        // Demo still wins when ExecutionLog is idle; they OR.
        vm.ExecutionLog.IsRunning = false;
        vm.IsDemoMode = true;
        nodeA.IsRunning.Should().BeTrue();
    }

    /// <summary>
    /// A station or pipe that arrives after running-state is already active
    /// (template load, undo of a delete, drag completion) must be seeded so
    /// it matches the surrounding animation instead of staying visually
    /// idle while everything around it is lit.
    /// </summary>
    [Fact]
    public void Node_added_while_demo_mode_is_on_is_seeded_as_running()
    {
        MainWindowViewModel vm = BuildVm(out _, out _);
        vm.IsDemoMode = true;

        PipelineNodeViewModel newNode = MakeNode("RenamePattern", vm);
        vm.Editor.Nodes.Add(newNode);

        newNode.IsRunning.Should().BeTrue();
    }

    /// <summary>
    /// Same contract for pipes. A connection created mid-run must not stay
    /// dark while the two stations it joins are glowing.
    /// </summary>
    [Fact]
    public void Connection_added_while_demo_mode_is_on_is_seeded_as_running()
    {
        MainWindowViewModel vm = BuildVm(out PipelineNodeViewModel nodeA, out PipelineNodeViewModel nodeB);
        vm.IsDemoMode = true;

        PipelineConnectionViewModel connection = AddConnection(vm, nodeA, nodeB);

        connection.IsRunning.Should().BeTrue();
    }

    /// <summary>
    /// ObservableCollection.Clear() raises the Reset action with no
    /// NewItems; the handler must still re-seed any nodes added after the
    /// clear, so a template swap or bulk LoadGraph during a demo leaves
    /// the fresh canvas lit.
    /// </summary>
    [Fact]
    public void Nodes_added_after_collection_reset_during_demo_are_seeded()
    {
        MainWindowViewModel vm = BuildVm(out _, out _);
        vm.IsDemoMode = true;

        // Clear() fires NotifyCollectionChangedAction.Reset with NewItems == null.
        vm.Editor.Nodes.Clear();
        vm.Editor.Connections.Clear();

        PipelineNodeViewModel freshNode = MakeNode("RenamePattern", vm);
        vm.Editor.Nodes.Add(freshNode);

        freshNode.IsRunning.Should().BeTrue(
            "nodes added after a Reset must still be seeded while demo mode is on");
    }

    /// <summary>
    /// Typing into a field is one undo step: after several keystrokes, one Undo brings back the
    /// value from before the first.
    /// </summary>
    [Fact]
    public void TypingOverValue_ThenOneUndo_RestoresOriginal()
    {
        using var temp = new TempDirectory();
        MainWindowViewModel vm = InspectRename(temp, new StubDialogService(), "x", out PipelineNodeViewModel rename);

        TypePattern(vm, "a", "ab", "abc");
        vm.UndoCommand.Execute(null);

        rename.Config["pattern"].GetString().Should().Be("x", "one undo reverts the whole typed edit");
    }

    /// <summary>
    /// Typing into a field that had no value, then one Undo, leaves it with no value again.
    /// </summary>
    [Fact]
    public void TypingOverAbsentKey_ThenOneUndo_RemovesKey()
    {
        using var temp = new TempDirectory();
        MainWindowViewModel vm = InspectRename(temp, new StubDialogService(), null, out PipelineNodeViewModel rename);

        TypePattern(vm, "a", "ab", "abc");
        vm.UndoCommand.Execute(null);

        rename.Config.Should().NotContainKey("pattern", "one undo removes the value the typing added");
    }

    /// <summary>
    /// Typing after a save starts a new undo step, so one Undo returns to the saved pipeline
    /// instead of past it.
    /// </summary>
    [Fact]
    public async Task EditAfterSave_ThenUndo_ReturnsToSavePoint()
    {
        using var temp = new TempDirectory();
        var dialog = new StubDialogService { SaveFileResult = Path.Combine(temp.Path, "typed.ffpipe") };
        MainWindowViewModel vm = InspectRename(temp, dialog, "x", out PipelineNodeViewModel rename);
        await vm.InitializeAsync();

        TypePattern(vm, "a", "ab");
        await vm.SaveCommand.ExecuteAsync(null);
        bool savedAtSavePoint = vm.Editor.UndoRedo.IsAtSavePoint;
        TypePattern(vm, "abc");
        bool typedAtSavePoint = vm.Editor.UndoRedo.IsAtSavePoint;
        vm.UndoCommand.Execute(null);

        savedAtSavePoint.Should().BeTrue("the save marks the typed value as saved");
        typedAtSavePoint.Should().BeFalse("typing after the save is an unsaved change");
        vm.Editor.UndoRedo.IsAtSavePoint.Should().BeTrue("one undo returns to the saved pipeline");
        rename.Config["pattern"].GetString().Should().Be("ab");
    }

    private static MainWindowViewModel BuildVm(
        out PipelineNodeViewModel nodeA,
        out PipelineNodeViewModel nodeB)
    {
        MainWindowViewModel vm = NewVm(
            Path.Combine(Path.GetTempPath(), $"ffsettings-{Guid.NewGuid():N}.json"),
            new StubDialogService());

        nodeA = MakeNode("FolderInput", vm);
        nodeB = MakeNode("FolderOutput", vm);
        vm.Editor.Nodes.Add(nodeA);
        vm.Editor.Nodes.Add(nodeB);
        return vm;
    }

    // A window whose inspector shows a Rename station through the production edit callback; its
    // pattern starts at the given text, or unset.
    private static MainWindowViewModel InspectRename(
        TempDirectory temp,
        StubDialogService dialog,
        string? pattern,
        out PipelineNodeViewModel rename)
    {
        MainWindowViewModel vm = NewVm(Path.Combine(temp.Path, "settings.json"), dialog);
        rename = MakeNode("RenamePattern", vm);
        if (pattern is not null)
        {
            rename.Config["pattern"] = JsonSerializer.SerializeToElement(pattern);
        }

        vm.Editor.Nodes.Add(rename);
        vm.Editor.SelectedNode = rename;
        return vm;
    }

    // Each value is one committed edit of the pattern field, as its text box sends them.
    private static void TypePattern(MainWindowViewModel vm, params string[] values)
    {
        ConfigFieldViewModel field = vm.Properties.Fields.Single(f => string.Equals(f.Key, "pattern", StringComparison.Ordinal));
        foreach (string value in values)
        {
            field.Value = value;
        }
    }

    private static MainWindowViewModel NewVm(string settingsPath, StubDialogService dialog)
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        var settings = new AppSettingsManager(settingsPath, NullLogger<AppSettingsManager>.Instance);
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        return new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            settings,
            registry,
            editor,
            dialog,
            services);
    }

    private static PipelineConnectionViewModel AddConnection(
        MainWindowViewModel vm,
        PipelineNodeViewModel source,
        PipelineNodeViewModel target)
    {
        PipelineConnectorViewModel output = source.Output.First();
        PipelineConnectorViewModel input = target.Input.First();
        var connection = new PipelineConnectionViewModel(output, input);
        vm.Editor.Connections.Add(connection);
        return connection;
    }

    private static PipelineNodeViewModel MakeNode(string typeKey, MainWindowViewModel vm)
    {
        var definition = new NodeDefinition
        {
            Id = Guid.NewGuid(),
            TypeKey = typeKey,
            Position = new CanvasPosition(0, 0),
            Config = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        };
        return new PipelineNodeViewModel(definition, vm.Registry);
    }
}
