using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FlowForge.UI;
using FlowForge.UI.Services;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The keys the shortcuts dialog lists reach the commands they name, and Save As is on the
/// toolbar as well as on its key. The live run sits only on a chord; Esc stops a run in
/// progress and otherwise passes on untouched.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ShortcutTests
{
    private const string SaveDialog = nameof(IDialogService.SaveFileAsync);
    private const string OpenDialog = nameof(IDialogService.OpenFileAsync);
    private const string Prompt = nameof(IDialogService.ConfirmUnsavedChangesAsync);
    private const string RunChord = "Ctrl+Shift+Enter";
    private const int InputFiles = 2;

    private static readonly TimeSpan RunDeadline = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    public ShortcutTests(ITestOutputHelper output) => _output = output;

    [AvaloniaTheory]
    [InlineData("Ctrl+N")]
    [InlineData("Ctrl+O")]
    [InlineData("Ctrl+S")]
    [InlineData("Ctrl+Shift+S")]
    public void FileShortcuts_InvokeTheirCommands(string gesture)
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        IReadOnlyList<(string Key, string Description)> rows = ShownShortcuts(session);
        rows.Select(row => row.Key).Should().Contain(gesture, "the shortcuts dialog lists the key");

        switch (gesture)
        {
            case "Ctrl+N":
                session.LoadTemplate(VisualSession.WebExportTemplate);
                session.Press(gesture);
                session.RenderUntil(() => vm.Editor.Nodes.Count == 0, "the loaded template cleared");
                Count(session, Prompt).Should().Be(1, "New asks before dropping the unsaved template");
                break;

            case "Ctrl+O":
                session.Press(gesture);
                session.RenderUntil(() => Count(session, OpenDialog) == 1, "a file asked for");
                break;

            case "Ctrl+S":
                session.LoadTemplate(VisualSession.WebExportTemplate);
                string path = Path.Combine(session.Temp.Path, "saved-by-key.ffpipe");
                File.WriteAllText(path, "not yet saved");
                vm.CurrentFilePath = path;
                session.Press(gesture);
                session.RenderUntil(() => !vm.IsDirty, "the pipeline saved");
                PipelineGraph saved = PipelineSerializer.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"saved: nodes={saved.Nodes.Count} editor={vm.Editor.Nodes.Count}");
                saved.Nodes.Should().HaveCount(vm.Editor.Nodes.Count, "the open file is rewritten with the pipeline");
                Count(session, SaveDialog).Should().Be(0, "a pipeline with a file saves to it without asking");
                break;

            case "Ctrl+Shift+S":
                session.Press(gesture);
                session.RenderUntil(() => Count(session, SaveDialog) == 1, "a file asked for");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(gesture), gesture, "No driver for this key.");
        }

        session.Pump();
        _output.WriteLine($"{gesture}: calls=[{string.Join(", ", session.Dialogs.Calls)}] nodes={vm.Editor.Nodes.Count} dirty={vm.IsDirty}");
    }

    [AvaloniaFact]
    public void SaveAs_ButtonAndShortcut_CallSaveFileOnce()
    {
        using var session = VisualSession.Open(_output);
        Button saveAs = session.ButtonNamed("Save As");
        string? tip = ToolTip.GetTip(saveAs) as string;
        _output.WriteLine($"tip='{tip}' bounds={session.BoundsInWindow(saveAs)}");
        tip.Should().Be("Save As (Ctrl+Shift+S)");

        session.Click(saveAs);
        session.RenderUntil(() => Count(session, SaveDialog) == 1, "the button asked for a file");
        session.Press("Ctrl+Shift+S");
        session.RenderUntil(() => Count(session, SaveDialog) == 2, "the key asked for a file");
        session.Pump();

        _output.WriteLine($"calls=[{string.Join(", ", session.Dialogs.Calls)}]");
        Count(session, SaveDialog).Should().Be(2, "the button and the key each ask once");
    }

    [AvaloniaFact]
    public void F5_PreviewsWithoutChangingFiles()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        ShownShortcuts(session).Select(row => row.Key).Should().Contain("F5", "the shortcuts dialog lists the key");
        string input = LoadCopyPipeline(session);
        var before = TreeSnapshot.Take(input);

        session.Press("F5");
        session.RenderUntil(() => RunFinished(vm), RunDeadline, "the preview finished");

        string[] delivered = Directory.GetFiles(session.Temp.OutputPath);
        _output.WriteLine($"F5: summary='{vm.ExecutionLog.Summary}' delivered={delivered.Length} live-run-started={vm.RunCommand.ExecutionTask is not null}");
        vm.ExecutionLog.Summary.Should().StartWith($"Preview complete: {InputFiles} files", "F5 is the dry run");
        vm.RunCommand.ExecutionTask.Should().BeNull("F5 never starts the live run");
        delivered.Should().BeEmpty("a preview writes nothing");
        TreeSnapshot.AssertUnchanged(before, TreeSnapshot.Take(input), _output, "input after F5");
    }

    [AvaloniaFact]
    public void LiveRun_StartsOnlyOnItsChord()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        ShownShortcuts(session).Select(row => row.Key).Should().Contain(RunChord, "the shortcuts dialog lists the chord");
        Button ignite = session.Window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, vm.RunCommand));
        string? tip = ToolTip.GetTip(ignite) as string;
        LoadCopyPipeline(session);

        foreach (string key in new[] { "Enter", "Ctrl+Enter" })
        {
            session.Press(key);
            session.Pump();
            _output.WriteLine($"{key}: run-started={vm.RunCommand.ExecutionTask is not null} preview-started={vm.DryRunCommand.ExecutionTask is not null} summary='{vm.ExecutionLog.Summary}'");
        }

        vm.RunCommand.ExecutionTask.Should().BeNull("neither Enter nor Ctrl+Enter starts the live run");
        vm.DryRunCommand.ExecutionTask.Should().BeNull("neither Enter nor Ctrl+Enter starts a preview");
        vm.ExecutionLog.Summary.Should().BeNull();

        session.Press(RunChord);
        session.RenderUntil(() => RunFinished(vm), RunDeadline, "the run finished");
        string[] delivered = Directory.GetFiles(session.Temp.OutputPath);
        _output.WriteLine($"{RunChord}: summary='{vm.ExecutionLog.Summary}' delivered={delivered.Length} ignite-tip='{tip}'");
        vm.ExecutionLog.Summary.Should().StartWith($"Run complete: {InputFiles} files", "the chord starts the live run");
        delivered.Should().HaveCount(InputFiles);
        tip.Should().Contain(RunChord, "IGNITE's tooltip names its chord");
    }

    [AvaloniaFact]
    public void Esc_StopsOnlyARunInProgress()
    {
        using var gate = new RunGate();
        using var session = VisualSession.Open(_output, loggerProvider: gate);
        MainWindowViewModel vm = session.ViewModel;
        ShownShortcuts(session).Select(row => row.Key).Should().Contain("Esc", "the shortcuts dialog lists the key");
        LoadCopyPipeline(session);

        // Counts each Esc that reaches the window still unhandled, the way every Esc did before.
        int passedOn = 0;
        session.Window.KeyDown += (_, args) => passedOn += args.Key == Key.Escape ? 1 : 0;

        session.Press("Escape");
        session.Pump();
        _output.WriteLine($"idle: passed-on={passedOn} running={vm.ExecutionLog.IsRunning} summary='{vm.ExecutionLog.Summary}' calls={session.Dialogs.Calls.Count} nodes={vm.Editor.Nodes.Count} dirty={vm.IsDirty}");
        passedOn.Should().Be(1, "with no run in progress Esc is left to whatever handles it otherwise");
        vm.ExecutionLog.IsRunning.Should().BeFalse();
        vm.ExecutionLog.Summary.Should().BeNull("Esc while idle changes nothing");
        session.Dialogs.Calls.Should().BeEmpty();
        vm.Editor.Nodes.Should().HaveCount(2);

        gate.Arm();
        vm.RunCommand.Execute(null);
        session.RenderUntil(() => gate.IsHolding, "the run held in progress");
        session.Press("Escape");
        gate.Release();
        session.RenderUntil(() => RunFinished(vm), RunDeadline, "the run ended");

        string[] delivered = Directory.GetFiles(session.Temp.OutputPath);
        _output.WriteLine($"running: summary='{vm.ExecutionLog.Summary}' delivered={delivered.Length}");
        vm.ExecutionLog.Summary.Should().Be("Execution cancelled.", "Esc stops the run in progress");
        delivered.Should().BeEmpty("the run stopped before delivering anything");
    }

    private static int Count(VisualSession session, string call) =>
        session.Dialogs.Calls.Count(name => string.Equals(name, call, StringComparison.Ordinal));

    private static bool RunFinished(MainWindowViewModel vm) =>
        !vm.ExecutionLog.IsRunning && vm.ExecutionLog.Summary is not null;

    // Two text files and a pipeline that copies them to the output folder, loaded into the editor.
    private static string LoadCopyPipeline(VisualSession session)
    {
        string input = Path.Combine(session.Temp.Path, "in");
        session.Temp.CreateFiles(Path.Combine("in", "a.txt"), Path.Combine("in", "b.txt"));
        PipelineGraph graph = PipelineBuilder.New()
            .AddSource("FolderInput", new { path = input, filter = "*.txt" })
            .AddOutput("FolderOutput", new { path = session.Temp.OutputPath, mode = "copy" })
            .Build();
        session.ViewModel.Editor.LoadGraph(graph, session.ViewModel.Registry).Should().Be(0, "the two stations connect");
        return input;
    }

    // Opens the shortcuts dialog the way a user does, reads its rows and closes it again. The
    // dialog must show its Close button whole, however many rows it lists.
    private IReadOnlyList<(string Key, string Description)> ShownShortcuts(VisualSession session)
    {
        MainWindow window = session.Window;
        session.Press("F1");
        session.RenderUntil(
            () => window.OwnedWindows.OfType<ShortcutsWindow>().Any(dialog => dialog.IsVisible && dialog.Bounds.Width > 0),
            "shortcuts dialog shown");
        ShortcutsWindow dialog = window.OwnedWindows.OfType<ShortcutsWindow>().Single();
        try
        {
            IReadOnlyList<(string Key, string Description)> rows = ShortcutRows.Read(dialog);
            _output.WriteLine($"[shortcuts] rows={rows.Count}: {string.Join(" | ", rows.Select(row => $"{row.Key} = {row.Description}"))}");
            PixelRect close = session.BoundsInWindow(dialog.GetVisualDescendants().OfType<Button>().Single());
            var client = new PixelRect(PixelSize.FromSize(dialog.ClientSize, dialog.RenderScaling));
            _output.WriteLine($"[shortcuts] close={close} client={client}");
            client.Contains(close).Should().BeTrue("the dialog's Close button is shown whole");
            return rows;
        }
        finally
        {
            dialog.Close();
            session.RenderUntil(() => window.OwnedWindows.Count == 0, "shortcuts dialog closed");
        }
    }

    /// <summary>
    /// A log sink that, once armed, holds the first message logged off the UI thread until it is
    /// released: a run logs from the thread pool, so it stays in progress while held.
    /// </summary>
    private sealed class RunGate : ILoggerProvider, ILogger
    {
        private static readonly TimeSpan HoldLimit = TimeSpan.FromSeconds(20);

        private readonly ManualResetEventSlim _released = new();
        private int _armed;
        private int _holding;

        public bool IsHolding => Volatile.Read(ref _holding) == 1;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public void Release() => _released.Set();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (Dispatcher.UIThread.CheckAccess() || Interlocked.Exchange(ref _armed, 0) == 0)
            {
                return;
            }

            Volatile.Write(ref _holding, 1);
            _released.Wait(HoldLimit);
        }

        public void Dispose()
        {
            _released.Set();
            _released.Dispose();
        }
    }
}
