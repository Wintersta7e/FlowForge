using System.Text.Json;
using Avalonia;
using Avalonia.Headless.XUnit;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using FlowForge.Tests.Visual;
using FlowForge.UI.Services;
using FlowForge.UI.UndoRedo;
using FlowForge.UI.UndoRedo.Commands;
using FlowForge.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.ViewModels;

/// <summary>
/// The editor knows when its pipeline differs from the last save, marks the title and file
/// name while it does, and asks before New, Open, Open recent, a template or closing would
/// throw that work away. The view model posts to the UI thread, so these run on the headless
/// dispatcher, which serves one test at a time.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class UnsavedChangesTests
{
    private const string Template = "image-web-export";
    private const string Marker = " *";
    private const string Prompt = nameof(IDialogService.ConfirmUnsavedChangesAsync);

    private static readonly string[] EntryPoints = ["New", "Open", "Open recent", "Template", "Close"];

    private readonly ITestOutputHelper _output;

    public UnsavedChangesTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [AvaloniaFact]
    public async Task EveryCommandType_SetsDirty()
    {
        var factories = new Dictionary<Type, Func<MainWindowViewModel, IUndoableCommand>>
        {
            [typeof(AddNodeCommand)] = vm => new AddNodeCommand(vm.Editor.Nodes, MakeNode(vm, "FolderInput")),
            [typeof(ChangeConfigCommand)] = vm => new ChangeConfigCommand(
                vm.Editor.Nodes[0].Config,
                "path",
                default,
                JsonSerializer.SerializeToElement("in"),
                keyExisted: false,
                "Change Path"),
            [typeof(CompositeCommand)] = vm => new CompositeCommand([Move(vm.Editor.Nodes[0])], "Move 1 node"),
            [typeof(ConnectCommand)] = vm => new ConnectCommand(
                vm.Editor.Connections,
                new PipelineConnectionViewModel(vm.Editor.Nodes[0].Output[0], vm.Editor.Nodes[1].Input[0])),
            [typeof(DisconnectCommand)] = vm =>
            {
                var connection = new PipelineConnectionViewModel(vm.Editor.Nodes[0].Output[0], vm.Editor.Nodes[1].Input[0]);
                vm.Editor.Connections.Add(connection);
                return new DisconnectCommand(vm.Editor.Connections, connection);
            },
            [typeof(MoveNodeCommand)] = vm => Move(vm.Editor.Nodes[0]),
            [typeof(RemoveNodesCommand)] = vm => new RemoveNodesCommand(vm.Editor.Nodes, vm.Editor.Connections, [vm.Editor.Nodes[1]], []),
        };
        var types = typeof(IUndoableCommand).Assembly.GetTypes()
            .Where(type => typeof(IUndoableCommand).IsAssignableFrom(type) && type is { IsClass: true, IsAbstract: false })
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();
        _output.WriteLine($"command-types={types.Count}: {string.Join(", ", types.Select(type => type.Name))}");

        types.Should().NotBeEmpty();
        types.Where(type => !factories.ContainsKey(type)).Should().BeEmpty("every undoable command type needs a case here");
        foreach (Type type in types)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            MainWindowViewModel vm = fixture.ViewModel;
            vm.Editor.Nodes.Add(MakeNode(vm, "FolderInput"));
            vm.Editor.Nodes.Add(MakeNode(vm, "FolderOutput"));
            bool before = vm.IsDirty;

            vm.Editor.UndoRedo.Execute(factories[type](vm));

            _output.WriteLine($"{type.Name}: before={before} after={vm.IsDirty}");
            before.Should().BeFalse("nothing was edited yet");
            vm.IsDirty.Should().BeTrue($"{type.Name} changes the pipeline");
        }
    }

    [AvaloniaFact]
    public async Task UndoToSavePoint_ClearsDirty_RedoSetsIt()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        MainWindowViewModel vm = fixture.ViewModel;
        await vm.LoadTemplateCommand.ExecuteAsync(Template);
        await fixture.SaveAsAsync("saved.ffpipe");
        vm.IsDirty.Should().BeFalse("the pipeline was just saved");

        vm.Editor.UndoRedo.Execute(Move(vm.Editor.Nodes[0]));
        vm.IsDirty.Should().BeTrue();
        vm.Editor.Undo();
        vm.IsDirty.Should().BeFalse("undo returned to the saved pipeline");
        vm.Editor.Redo();
        vm.IsDirty.Should().BeTrue("redo left the saved pipeline again");
    }

    [AvaloniaFact]
    public async Task CoalescedEditAfterSave_SetsDirty()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        MainWindowViewModel vm = fixture.ViewModel;
        await vm.LoadTemplateCommand.ExecuteAsync(Template);
        vm.Editor.SelectedNode = vm.Editor.Nodes[0];
        vm.Properties.Fields.Single(field => string.Equals(field.Key, "path", StringComparison.Ordinal)).Value = "a";
        await fixture.SaveAsAsync("saved.ffpipe");
        vm.IsDirty.Should().BeFalse();
        string? before = vm.Editor.UndoRedo.UndoDescription;

        vm.Editor.SelectedNode = null;
        vm.Editor.SelectedNode = vm.Editor.Nodes[0];
        vm.Properties.Fields.Single(field => string.Equals(field.Key, "path", StringComparison.Ordinal)).Value = "ab";

        _output.WriteLine($"undo before='{before}' after='{vm.Editor.UndoRedo.UndoDescription}' dirty={vm.IsDirty}");
        vm.IsDirty.Should().BeTrue("the next keystroke merged into the saved edit and changed the pipeline");
    }

    [AvaloniaFact]
    public async Task TwentySixEditsThenTwentySixUndos_StayDirty()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        MainWindowViewModel vm = fixture.ViewModel;
        await vm.LoadTemplateCommand.ExecuteAsync(Template);
        await fixture.SaveAsAsync("saved.ffpipe");

        for (int i = 0; i < 26; i++)
        {
            vm.Editor.UndoRedo.Execute(Move(vm.Editor.Nodes[0]));
        }

        for (int i = 0; i < 26; i++)
        {
            vm.Editor.Undo();
        }

        _output.WriteLine($"can-undo={vm.Editor.UndoRedo.CanUndo} dirty={vm.IsDirty}");
        vm.IsDirty.Should().BeTrue("the oldest edit fell out of the history, so undo cannot reach the saved pipeline");
    }

    [AvaloniaFact]
    public async Task FreshSessionClean_TemplateLoadDirty_SaveClean()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        MainWindowViewModel vm = fixture.ViewModel;
        vm.IsDirty.Should().BeFalse("a fresh window has nothing to lose");

        await vm.LoadTemplateCommand.ExecuteAsync(Template);
        vm.IsDirty.Should().BeTrue("a template is new, unsaved work");

        fixture.Dialogs.SaveFileResult = Path.Combine(fixture.Temp.Path, "saved.ffpipe");
        await vm.SaveCommand.ExecuteAsync(null);
        fixture.Dialogs.Calls.Should().Equal(nameof(IDialogService.SaveFileAsync));
        vm.IsDirty.Should().BeFalse("the template was saved");
        vm.CurrentFilePath.Should().Be(fixture.Dialogs.SaveFileResult);
    }

    [AvaloniaFact]
    public async Task DirtyMarker_AppendsToTitleAndDocumentName()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        MainWindowViewModel vm = fixture.ViewModel;
        var raised = new List<string>();
        vm.PropertyChanged += (_, args) => raised.Add(args.PropertyName ?? string.Empty);
        _output.WriteLine($"fresh: title='{vm.Title}' name='{vm.DocumentName}'");
        vm.Title.Should().NotEndWith(Marker);
        vm.DocumentName.Should().Be("untitled.ffpipe");

        await vm.LoadTemplateCommand.ExecuteAsync(Template);
        _output.WriteLine($"template: title='{vm.Title}' name='{vm.DocumentName}' raised=[{string.Join(", ", raised.Distinct(StringComparer.Ordinal))}]");
        vm.Title.Should().EndWith(Marker);
        vm.DocumentName.Should().Be("untitled.ffpipe" + Marker);
        raised.Should().Contain([nameof(MainWindowViewModel.IsDirty), nameof(MainWindowViewModel.Title), nameof(MainWindowViewModel.DocumentName)]);

        await fixture.SaveAsAsync("saved.ffpipe");
        _output.WriteLine($"saved: title='{vm.Title}' name='{vm.DocumentName}'");
        vm.Title.Should().Be("FlowForge - saved");
        vm.DocumentName.Should().Be("saved.ffpipe");
    }

    [AvaloniaFact]
    public async Task EveryEntryPoint_PromptsOnceWhileDirty()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            await fixture.ViewModel.LoadTemplateCommand.ExecuteAsync(Template);
            fixture.Dialogs.ConfirmResult = UnsavedChangesChoice.Cancel;
            int before = fixture.Prompts;

            await fixture.RunAsync(entryPoint);

            int prompts = fixture.Prompts - before;
            _output.WriteLine($"{entryPoint}: prompts={prompts} name='{fixture.Dialogs.LastPipelineName}'");
            prompts.Should().Be(1, $"{entryPoint} would discard unsaved work");
            fixture.Dialogs.LastPipelineName.Should().Be("untitled.ffpipe", "the prompt names the pipeline without the unsaved marker");
        }
    }

    [AvaloniaFact]
    public async Task Cancel_LeavesGraphPathAndHistory()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            MainWindowViewModel vm = fixture.ViewModel;
            await vm.LoadTemplateCommand.ExecuteAsync(Template);
            await fixture.SaveAsAsync("saved.ffpipe");
            vm.Editor.UndoRedo.Execute(Move(vm.Editor.Nodes[0]));
            var before = State.Of(vm);
            fixture.Dialogs.ConfirmResult = UnsavedChangesChoice.Cancel;
            int calls = fixture.Dialogs.Calls.Count;

            bool proceeded = await fixture.RunAsync(entryPoint);

            _output.WriteLine($"{entryPoint}: calls=[{string.Join(", ", fixture.Dialogs.Calls.Skip(calls))}] proceeded={proceeded}");
            fixture.Dialogs.Calls.Skip(calls).Should().Equal([Prompt], $"{entryPoint} stops at the prompt");
            before.ShouldMatch(vm, $"cancelling {entryPoint} keeps the pipeline as it was");
            vm.IsDirty.Should().BeTrue();
            if (string.Equals(entryPoint, "Close", StringComparison.Ordinal))
            {
                proceeded.Should().BeFalse("cancel keeps the window open");
            }
        }
    }

    [AvaloniaFact]
    public async Task SaveWithCancelledSaveAs_LeavesGraphPathAndHistory()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            MainWindowViewModel vm = fixture.ViewModel;
            await vm.LoadTemplateCommand.ExecuteAsync(Template);
            vm.Editor.UndoRedo.Execute(Move(vm.Editor.Nodes[0]));
            var before = State.Of(vm);
            fixture.Dialogs.ConfirmResult = UnsavedChangesChoice.Save;
            fixture.Dialogs.SaveFileResult = null;
            int calls = fixture.Dialogs.Calls.Count;

            bool proceeded = await fixture.RunAsync(entryPoint);

            _output.WriteLine($"{entryPoint}: calls=[{string.Join(", ", fixture.Dialogs.Calls.Skip(calls))}] proceeded={proceeded}");
            fixture.Dialogs.Calls.Skip(calls).Should().Equal(
                [Prompt, nameof(IDialogService.SaveFileAsync)],
                $"an untitled pipeline is saved through Save As before {entryPoint}");
            before.ShouldMatch(vm, $"a cancelled save must not let {entryPoint} discard the work");
            vm.IsDirty.Should().BeTrue();
            if (string.Equals(entryPoint, "Close", StringComparison.Ordinal))
            {
                proceeded.Should().BeFalse("the work was not saved, so the window stays open");
            }
        }
    }

    [AvaloniaFact]
    public async Task Save_WritesTheFileThenProceeds()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            MainWindowViewModel vm = fixture.ViewModel;
            await vm.LoadTemplateCommand.ExecuteAsync(Template);
            int templateNodes = vm.Editor.Nodes.Count;
            string saved = Path.Combine(fixture.Temp.Path, "saved.ffpipe");
            fixture.Dialogs.ConfirmResult = UnsavedChangesChoice.Save;
            fixture.Dialogs.SaveFileResult = saved;

            bool proceeded = await fixture.RunAsync(entryPoint);

            PipelineGraph written = await PipelineSerializer.LoadAsync(saved);
            _output.WriteLine($"{entryPoint}: saved-nodes={written.Nodes.Count} proceeded={proceeded} nodes={vm.Editor.Nodes.Count} path='{Path.GetFileName(vm.CurrentFilePath)}'");
            written.Nodes.Should().HaveCount(templateNodes, "the work was saved before anything replaced it");
            proceeded.Should().BeTrue($"{entryPoint} goes on once the work is saved");
        }
    }

    [AvaloniaFact]
    public async Task Discard_Proceeds()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            MainWindowViewModel vm = fixture.ViewModel;
            await vm.LoadTemplateCommand.ExecuteAsync(Template);
            vm.Editor.UndoRedo.Execute(Move(vm.Editor.Nodes[0]));
            var before = State.Of(vm);
            fixture.Dialogs.ConfirmResult = UnsavedChangesChoice.Discard;

            bool proceeded = await fixture.RunAsync(entryPoint);

            _output.WriteLine($"{entryPoint}: proceeded={proceeded} nodes={vm.Editor.Nodes.Count} path='{Path.GetFileName(vm.CurrentFilePath)}' dirty={vm.IsDirty}");
            proceeded.Should().BeTrue();
            if (!string.Equals(entryPoint, "Close", StringComparison.Ordinal))
            {
                vm.Editor.Nodes.Should().NotIntersectWith(before.Nodes, $"{entryPoint} replaced the discarded pipeline");
            }
        }
    }

    [AvaloniaFact]
    public async Task Clean_NoEntryPointPrompts()
    {
        foreach (string entryPoint in EntryPoints)
        {
            using Fixture fixture = await Fixture.CreateAsync();
            fixture.ViewModel.IsDirty.Should().BeFalse();

            bool proceeded = await fixture.RunAsync(entryPoint);

            _output.WriteLine($"{entryPoint}: prompts={fixture.Prompts} proceeded={proceeded}");
            fixture.Prompts.Should().Be(0, $"{entryPoint} has nothing to lose in a clean window");
            proceeded.Should().BeTrue();
        }
    }

    private static MoveNodeCommand Move(PipelineNodeViewModel node) =>
        new(node, node.Location, node.Location + new Point(10, 0));

    private static PipelineNodeViewModel MakeNode(MainWindowViewModel vm, string typeKey) =>
        new(
            new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeKey = typeKey,
                Position = new CanvasPosition(0, 0),
                Config = new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            },
            vm.Registry);

    // What a cancelled action must leave alone: the stations and pipes, the open file and the history.
    private sealed record State(
        IReadOnlyList<PipelineNodeViewModel> Nodes,
        IReadOnlyList<PipelineConnectionViewModel> Connections,
        string? Path,
        string? UndoDescription,
        bool CanRedo)
    {
        public static State Of(MainWindowViewModel vm) => new(
            [.. vm.Editor.Nodes],
            [.. vm.Editor.Connections],
            vm.CurrentFilePath,
            vm.Editor.UndoRedo.UndoDescription,
            vm.Editor.UndoRedo.CanRedo);

        public void ShouldMatch(MainWindowViewModel vm, string because)
        {
            vm.Editor.Nodes.Should().Equal(Nodes, because);
            vm.Editor.Connections.Should().Equal(Connections, because);
            vm.CurrentFilePath.Should().Be(Path, because);
            vm.Editor.UndoRedo.UndoDescription.Should().Be(UndoDescription, because);
            vm.Editor.UndoRedo.CanRedo.Should().Be(CanRedo, because);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services;

        private Fixture(TempDirectory temp, StubDialogService dialogs, MainWindowViewModel viewModel, ServiceProvider services, string otherFile)
        {
            Temp = temp;
            Dialogs = dialogs;
            ViewModel = viewModel;
            _services = services;
            OtherFile = otherFile;
        }

        public TempDirectory Temp { get; }

        public StubDialogService Dialogs { get; }

        public MainWindowViewModel ViewModel { get; }

        // A saved pipeline that Open and Open recent load.
        public string OtherFile { get; }

        public int Prompts => Dialogs.Calls.Count(call => string.Equals(call, Prompt, StringComparison.Ordinal));

        public static async Task<Fixture> CreateAsync()
        {
            var temp = new TempDirectory();
            var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
            var settings = new AppSettingsManager(Path.Combine(temp.Path, "settings.json"), NullLogger<AppSettingsManager>.Instance);
            var editor = new EditorViewModel(NullLogger<EditorViewModel>.Instance, registry);
            var dialogs = new StubDialogService();
            ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            var vm = new MainWindowViewModel(NullLogger<MainWindowViewModel>.Instance, settings, registry, editor, dialogs, services);
            await vm.InitializeAsync();

            string other = Path.Combine(temp.Path, "other.ffpipe");
            PipelineGraph graph = PipelineBuilder.New("Other")
                .AddSource("FolderInput", new { path = temp.Path, filter = "*" })
                .AddOutput("FolderOutput", new { path = temp.OutputPath, mode = "copy" })
                .Build();
            await PipelineSerializer.SaveAsync(graph, other);
            dialogs.OpenFileResult = other;
            return new Fixture(temp, dialogs, vm, services, other);
        }

        public async Task SaveAsAsync(string fileName)
        {
            Dialogs.SaveFileResult = Path.Combine(Temp.Path, fileName);
            await ViewModel.SaveAsCommand.ExecuteAsync(null);
            ViewModel.CurrentFilePath.Should().Be(Dialogs.SaveFileResult);
        }

        // Runs one guarded action; true when it went ahead (for Close, when the window may close).
        public async Task<bool> RunAsync(string entryPoint)
        {
            switch (entryPoint)
            {
                case "New":
                    await ViewModel.NewCommand.ExecuteAsync(null);
                    return ViewModel.Editor.Nodes.Count == 0;
                case "Open":
                    await ViewModel.OpenCommand.ExecuteAsync(null);
                    return string.Equals(ViewModel.CurrentFilePath, OtherFile, StringComparison.Ordinal);
                case "Open recent":
                    await ViewModel.OpenRecentCommand.ExecuteAsync(OtherFile);
                    return string.Equals(ViewModel.CurrentFilePath, OtherFile, StringComparison.Ordinal);
                case "Template":
                    IReadOnlyList<PipelineNodeViewModel> nodes = [.. ViewModel.Editor.Nodes];
                    await ViewModel.LoadTemplateCommand.ExecuteAsync(Template);
                    return ViewModel.Editor.Nodes.Count > 0 && !ViewModel.Editor.Nodes.Intersect(nodes).Any();
                default:
                    return await ViewModel.ConfirmCloseAsync();
            }
        }

        public void Dispose()
        {
            _services.Dispose();
            Temp.Dispose();
        }
    }
}
