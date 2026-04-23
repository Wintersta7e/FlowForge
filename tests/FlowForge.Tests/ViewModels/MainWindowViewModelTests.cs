using System.Text.Json;
using FluentAssertions;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Settings;
using FlowForge.UI.Services;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.ViewModels;

public class MainWindowViewModelTests
{
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

    private static MainWindowViewModel BuildVm(
        out PipelineNodeViewModel nodeA,
        out PipelineNodeViewModel nodeB)
    {
        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        var settings = new AppSettingsManager(
            Path.Combine(Path.GetTempPath(), $"ffsettings-{Guid.NewGuid():N}.json"),
            NullLogger<AppSettingsManager>.Instance);
        var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance);
        var dialog = new StubDialogService();
        ServiceProvider services = new ServiceCollection().BuildServiceProvider();

        var vm = new MainWindowViewModel(
            NullLogger<MainWindowViewModel>.Instance,
            settings,
            registry,
            editor,
            dialog,
            services);

        nodeA = MakeNode("FolderInput", vm);
        nodeB = MakeNode("FolderOutput", vm);
        vm.Editor.Nodes.Add(nodeA);
        vm.Editor.Nodes.Add(nodeB);
        return vm;
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

    private sealed class StubDialogService : IDialogService
    {
        public Task<string?> OpenFileAsync(string title, string filter) => Task.FromResult<string?>(null);

        public Task<string?> SaveFileAsync(string title, string filter, string? defaultName) => Task.FromResult<string?>(null);

        public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);
    }
}
