using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
using Nodify.Avalonia;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The keys the shortcuts dialog lists reach the commands they name, and Save As is on the
/// toolbar as well as on its key. The live run sits only on a chord; Esc stops a run in
/// progress and otherwise passes on untouched. Every row the dialog lists, canvas gestures
/// included, does what its description says, and every key the window acts on is listed.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed partial class ShortcutTests
{
    private const string SaveDialog = nameof(IDialogService.SaveFileAsync);
    private const string OpenDialog = nameof(IDialogService.OpenFileAsync);
    private const string Prompt = nameof(IDialogService.ConfirmUnsavedChangesAsync);
    private const string RunChord = "Ctrl+Shift+Enter";
    private const string Produced = "effect";
    private const int InputFiles = 2;

    private static readonly TimeSpan RunDeadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan EffectDeadline = TimeSpan.FromSeconds(3);
    private static readonly Vector DragBy = new(120, 80);

    // What each row's description promises. Arrange sets a fresh session up and returns the check,
    // read after the gesture until it answers: true once the promise is kept, false when the run
    // ended without it, null while it cannot tell yet.
    private static readonly Dictionary<string, Effect> Effects = new(StringComparer.Ordinal)
    {
        ["New pipeline"] = new((session, _) =>
        {
            session.LoadTemplate(VisualSession.WebExportTemplate);
            return Seen(() => session.ViewModel.Editor.Nodes.Count == 0);
        }),
        ["Open pipeline"] = new((session, _) => Seen(() => Count(session, OpenDialog) > 0)),
        ["Save"] = new((session, _) =>
        {
            session.LoadTemplate(VisualSession.WebExportTemplate);
            session.ViewModel.CurrentFilePath = Path.Combine(session.Temp.Path, "saved-by-key.ffpipe");
            return Seen(() => !session.ViewModel.IsDirty);
        }),
        ["Save as"] = new((session, _) => Seen(() => Count(session, SaveDialog) > 0)),
        ["Remove selected nodes"] = new((session, _) =>
        {
            EditorViewModel editor = session.ViewModel.Editor;
            session.LoadTemplate(VisualSession.WebExportTemplate);
            session.Click(session.WaitForStation(editor.Nodes[1]));
            session.RenderUntil(() => editor.SelectedNode is not null, "a station selected");
            int before = editor.Nodes.Count;
            return Seen(() => editor.Nodes.Count == before - 1);
        }),
        ["Zoom to fit all nodes"] = new((session, _) =>
        {
            // The template's last stations start off screen.
            session.LoadTemplate(VisualSession.WebExportTemplate);
            return Seen(session.StationsInsideCanvas);
        }),
        ["Pan canvas"] = new((session, _) =>
        {
            session.LoadTemplate(VisualSession.WebExportTemplate);
            NodifyEditor canvas = session.Canvas();
            Point before = canvas.ViewportLocation;
            return Seen(() => canvas.ViewportLocation != before);
        }),
        ["Zoom in/out"] = new((session, _) =>
        {
            session.LoadTemplate(VisualSession.WebExportTemplate);
            NodifyEditor canvas = session.Canvas();
            double before = canvas.ViewportZoom;
            return Seen(() => canvas.ViewportZoom != before);
        }),
        ["Undo"] = new((session, _) =>
        {
            session.ViewModel.Editor.AddNode("Sort", new Point(200, 160), session.ViewModel.Registry);
            return Seen(() => session.ViewModel.Editor.Nodes.Count == 0);
        }),
        ["Redo"] = new((session, _) =>
        {
            session.ViewModel.Editor.AddNode("Sort", new Point(200, 160), session.ViewModel.Registry);
            session.ViewModel.Editor.Undo();
            return Seen(() => session.ViewModel.Editor.Nodes.Count == 1);
        }),
        ["Dry run (no files changed)"] = new(
            (session, _) =>
            {
                LoadCopyPipeline(session);
                return () => RunFinished(session.ViewModel)
                    ? session.ViewModel.ExecutionLog.Summary!.StartsWith("Preview complete", StringComparison.Ordinal)
                        && Directory.GetFiles(session.Temp.OutputPath).Length == 0
                    : null;
            },
            RunDeadline),
        ["Run pipeline"] = new(
            (session, _) =>
            {
                LoadCopyPipeline(session);
                return () => RunFinished(session.ViewModel)
                    ? session.ViewModel.ExecutionLog.Summary!.StartsWith("Run complete", StringComparison.Ordinal)
                        && Directory.GetFiles(session.Temp.OutputPath).Length == InputFiles
                    : null;
            },
            RunDeadline),
        ["Stop the running pipeline"] = new(
            (session, gate) =>
            {
                LoadCopyPipeline(session);
                gate.Arm();
                session.ViewModel.RunCommand.Execute(null);
                session.RenderUntil(() => gate.IsHolding, "the run held in progress");
                return () => RunFinished(session.ViewModel)
                    ? string.Equals(session.ViewModel.ExecutionLog.Summary, "Execution cancelled.", StringComparison.Ordinal)
                    : null;
            },
            RunDeadline),
        ["Show this help"] = new((session, _) =>
            Seen(() => session.Window.OwnedWindows.OfType<ShortcutsWindow>().Any(dialog => dialog.IsVisible))),
        ["Disconnect"] = new((session, _) =>
        {
            LoadCopyPipeline(session);
            EditorViewModel editor = session.ViewModel.Editor;
            int before = editor.Connections.Count;
            return Seen(() => editor.Connections.Count < before);
        }),
    };

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

    // Each row's key text is read as a gesture and its description as an effect; each gesture is
    // sent in a fresh session set up for that effect. A row this test cannot drive fails it.
    [AvaloniaFact]
    public void EveryListedShortcut_ProducesItsEffect()
    {
        IReadOnlyList<(string Key, string Description)> rows;
        using (var listing = VisualSession.Open(_output))
        {
            rows = ShownShortcuts(listing);
        }

        var outcomes = new List<string>();
        var failures = new List<string>();
        foreach ((string key, string description) in rows)
        {
            List<(string Name, Action<VisualSession> Send)>? gestures = Gestures(key);
            Effect? effect = Effects.GetValueOrDefault(description);
            if (gestures is null || effect is null)
            {
                failures.Add($"{key} = {description}: no driver for its {(gestures is null ? "key" : "description")}");
                continue;
            }

            foreach ((string name, Action<VisualSession> send) in gestures)
            {
                string result = Drive(name, description, effect, send);
                outcomes.Add($"{name} = {description}: {result}");
                if (!string.Equals(result, Produced, StringComparison.Ordinal))
                {
                    failures.Add(outcomes[^1]);
                }
            }
        }

        _output.WriteLine($"[shortcuts] rows={rows.Count} gestures={outcomes.Count}");
        outcomes.ForEach(_output.WriteLine);
        failures.Should().BeEmpty("every row the shortcuts dialog lists does what it says, and these do not: {0}", string.Join(" | ", failures));
    }

    // The keys the window acts on come from the window itself: the gestures of its key bindings,
    // and every key and modifier set its key-down handling marks handled, sent to it one at a time.
    // Keys a plain window handles the same way (Tab moves the focus) are the framework's, not the
    // app's. A key handled under every modifier set is one shortcut, listed by its bare key.
    [AvaloniaFact]
    public void EveryHandledShortcut_IsListed()
    {
        using var session = VisualSession.Open(_output);
        var listed = ShownShortcuts(session).Select(row => ListedGesture(row.Key)).OfType<KeyGesture>().ToHashSet();
        MainWindow window = session.Window;
        var plain = new Window { Content = new Button { Content = "focusable" } };
        plain.Show();
        List<KeyGesture> framework;
        try
        {
            framework = HandledKeys(plain);
        }
        finally
        {
            plain.Close();
            Dispatcher.UIThread.RunJobs();
        }

        var swept = HandledKeys(window).Except(framework).ToList();
        var handled = window.KeyBindings.Select(binding => binding.Gesture).OfType<KeyGesture>().Concat(swept).ToList();
        string[] unlisted = handled.Where(gesture => !listed.Contains(gesture))
            .Select(gesture => gesture.ToString(null, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        _output.WriteLine($"[shortcuts] bindings={window.KeyBindings.Count} swept={swept.Count} framework={framework.Count} ({string.Join(", ", framework)})");
        _output.WriteLine($"[shortcuts] handled={handled.Count}: {string.Join(", ", handled)}");
        _output.WriteLine($"[shortcuts] listed={listed.Count}: {string.Join(", ", listed)}");
        _output.WriteLine($"[shortcuts] unlisted={unlisted.Length}: {string.Join(", ", unlisted)}");
        swept.Should().NotBeEmpty("the window handles some keys in its own key-down handling");
        unlisted.Should().BeEmpty("every key the window acts on is listed in the shortcuts dialog");
    }

    // Every key, under each of the 16 modifier sets, sent to the window once; the gestures it handled.
    private static List<KeyGesture> HandledKeys(Window window)
    {
        KeyModifiers[] modifierSets = Enumerable.Range(0, 16).Select(bits => (KeyModifiers)bits).ToArray();
        var handled = new List<KeyGesture>();
        foreach (Key key in Enum.GetValues<Key>().Distinct().Where(key => key != Key.None))
        {
            KeyGesture[] byKey = modifierSets.Where(modifiers => HandlesKeyDown(window, key, modifiers))
                .Select(modifiers => new KeyGesture(key, modifiers)).ToArray();
            handled.AddRange(byKey.Length == modifierSets.Length ? [new KeyGesture(key)] : byKey);
        }

        return handled;
    }

    // Sends one key-down to the window and reads whether it was handled. F1 marks it handled only
    // once the dialog it opens is closed, so a dialog it opened is closed before the read.
    private static bool HandlesKeyDown(Window window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = window };
        window.RaiseEvent(args);
        foreach (Window dialog in window.OwnedWindows.ToList())
        {
            dialog.Close();
        }

        Dispatcher.UIThread.RunJobs();
        return args.Handled;
    }

    // The key gesture a row's key text names; null for a pointer row.
    private static KeyGesture? ListedGesture(string keyText) =>
        ButtonDrag().IsMatch(keyText) || HeldKeyDrag().IsMatch(keyText) || PointerAction().IsMatch(keyText)
            ? null
            : KeyGesture.Parse(GestureText(keyText));

    private static Func<bool?> Seen(Func<bool> condition) => () => condition() ? true : null;

    // The input a row's key text names, once per way of giving it ("Middle/Right-drag" is two
    // drags); null when the text names nothing this test can send.
    private static List<(string Name, Action<VisualSession> Send)>? Gestures(string keyText)
    {
        if (ButtonDrag().Match(keyText) is { Success: true } drag)
        {
            var drags = new List<(string Name, Action<VisualSession> Send)>();
            foreach (string name in drag.Groups["buttons"].Value.Split('/'))
            {
                if (!Enum.TryParse(name, out MouseButton button))
                {
                    return null;
                }

                drags.Add(($"{name}-drag", session => session.Drag(session.OpenCanvasPoint(), DragBy, button)));
            }

            return drags;
        }

        if (HeldKeyDrag().Match(keyText) is { Success: true } held)
        {
            if (!Enum.TryParse(held.Groups["key"].Value, out Key key))
            {
                return null;
            }

            PhysicalKey physical = Enum.TryParse(key.ToString(), out PhysicalKey named) ? named : PhysicalKey.None;
            return [(keyText, session =>
            {
                session.Window.KeyPress(key, RawInputModifiers.None, physical, keySymbol: null);
                session.Drag(session.OpenCanvasPoint(), DragBy, MouseButton.Left);
                session.Window.KeyRelease(key, RawInputModifiers.None, physical, keySymbol: null);
            })];
        }

        if (PointerAction().Match(keyText) is { Success: true } pointer)
        {
            if (Modifiers(pointer.Groups["modifiers"].Value) is not { } modifiers)
            {
                return null;
            }

            return string.Equals(pointer.Groups["action"].Value, "Scroll", StringComparison.Ordinal)
                ? [(keyText, session => session.Window.MouseWheel(session.OpenCanvasPoint(), new Vector(0, 1), modifiers))]
                : [(keyText, session => ClickPort(session, modifiers))];
        }

        string gesture = GestureText(keyText);
        try
        {
            KeyGesture.Parse(gesture);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }

        return [(keyText, session => session.Press(gesture))];
    }

    // The dialog writes keys as people say them; a key gesture names these two differently.
    private static string GestureText(string keyText)
    {
        int plus = keyText.LastIndexOf('+');
        string key = keyText[(plus + 1)..];
        string named = key switch
        {
            "Esc" => "Escape",
            [char digit] when char.IsAsciiDigit(digit) => "D" + digit,
            _ => key,
        };
        return keyText[..(plus + 1)] + named;
    }

    private static RawInputModifiers? Modifiers(string text)
    {
        RawInputModifiers modifiers = RawInputModifiers.None;
        foreach (string name in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            RawInputModifiers? one = name switch
            {
                "Ctrl" => RawInputModifiers.Control,
                "Shift" => RawInputModifiers.Shift,
                "Alt" => RawInputModifiers.Alt,
                _ => null,
            };
            if (one is null)
            {
                return null;
            }

            modifiers |= one.Value;
        }

        return modifiers;
    }

    // A press and release on the source port of the editor's first pipe, once the pointer is on it.
    private static void ClickPort(VisualSession session, RawInputModifiers modifiers)
    {
        Point centre = session.CentreOf(session.WaitForPort(session.ViewModel.Editor.Connections[0].Source));
        session.Window.MouseMove(centre, RawInputModifiers.None);
        session.Window.MouseDown(centre, MouseButton.Left, modifiers);
        session.Window.MouseUp(centre, MouseButton.Left, modifiers);
    }

    [GeneratedRegex(@"^(?<buttons>\w+(/\w+)*)-drag$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ButtonDrag();

    [GeneratedRegex(@"^(?<key>\w+)\+Drag$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeldKeyDrag();

    [GeneratedRegex(@"^((?<modifiers>.+)\+)?(?<action>Scroll|Click port)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PointerAction();

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

    // A fresh session set up for the effect; sends the gesture and reads the check until it
    // answers. An effect that already held before the gesture proves nothing.
    private string Drive(string gesture, string description, Effect effect, Action<VisualSession> send)
    {
        using var gate = new RunGate();
        using var session = VisualSession.Open(_output, loggerProvider: gate);
        Func<bool?> check = effect.Arrange(session, gate);
        if (check() == true)
        {
            return "already true before the gesture";
        }

        send(session);

        // A run the arrangement holds in progress goes on once the gesture is sent.
        gate.Release();
        bool? answer = null;
        session.TryRenderUntil(() => (answer = check()) is not null, effect.Deadline ?? EffectDeadline, $"{gesture}: {description}");
        return answer == true ? Produced : "no effect";
    }

    /// <summary>
    /// What a row's description promises: <see cref="Arrange"/> sets a fresh session up and
    /// returns the check read after the gesture.
    /// </summary>
    private sealed record Effect(Func<VisualSession, RunGate, Func<bool?>> Arrange, TimeSpan? Deadline = null);

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
