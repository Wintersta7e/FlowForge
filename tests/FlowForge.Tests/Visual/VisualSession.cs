using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FlowForge.Core.Execution;
using FlowForge.Core.Settings;
using FlowForge.Tests.Helpers;
using FlowForge.UI;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodify.Avalonia;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace FlowForge.Tests.Visual;

/// <summary>
/// One real themed main window, rendered headless in-process, with every input the view
/// model reads isolated in a temp directory. Tests reach a state by pumping the dispatcher
/// and the render timer until a condition holds, never by waiting on the clock.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Pump, the frame readers and the input helpers are session members: tests reach every harness step through the session they opened.")]
internal sealed class VisualSession : IDisposable
{
    public const string WebExportTemplate = "image-web-export";

    private const string ConfigHomeVariable = "XDG_CONFIG_HOME";

    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(3);
    private static readonly Lazy<string> ClearedFrameDirectory = new(ClearFrameDirectory);
    private static readonly Lock WrittenFramesLock = new();
    private static readonly HashSet<string> WrittenFrames = new(StringComparer.Ordinal);

    // Every window currently open in the process, kept by class handlers on the UI thread.
    private static readonly HashSet<Window> OpenWindows = [];

    private readonly ITestOutputHelper _output;
    private readonly bool _configHomeSet;
    private readonly string? _previousConfigHome;
    private readonly ServiceProvider? _provider;
    private bool _disposed;

    static VisualSession()
    {
        Avalonia.Controls.Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => OpenWindows.Add(window));
        Avalonia.Controls.Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => OpenWindows.Remove(window));
    }

    private VisualSession(ITestOutputHelper output, Size? window, ILoggerProvider? loggerProvider)
    {
        _output = output;
        Temp = new TempDirectory();
        SettingsPath = Path.Combine(Temp.Path, "settings.json");
        try
        {
            // Before anything reads the user's config folder: a directory that does not
            // exist would make the folder lookup fall back to a relative path.
            if (OperatingSystem.IsLinux())
            {
                string configHome = Path.Combine(Temp.Path, "xdg");
                Directory.CreateDirectory(configHome);
                _previousConfigHome = Environment.GetEnvironmentVariable(ConfigHomeVariable);
                Environment.SetEnvironmentVariable(ConfigHomeVariable, configHome);
                _configHomeSet = true;
                _output.WriteLine($"{ConfigHomeVariable}={configHome}");
            }

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                if (loggerProvider is not null)
                {
                    builder.AddProvider(loggerProvider);
                }
            });
            services.AddSingleton(sp => NodeRegistry.CreateDefault(sp.GetRequiredService<ILoggerFactory>()));
            services.AddTransient(sp => new PipelineRunner(
                sp.GetRequiredService<NodeRegistry>(),
                sp.GetRequiredService<ILogger<PipelineRunner>>()));
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            Dialogs = new StubDialogService();
            ViewModel = new MainWindowViewModel(
                _provider.GetRequiredService<ILogger<MainWindowViewModel>>(),
                new AppSettingsManager(SettingsPath, _provider.GetRequiredService<ILogger<AppSettingsManager>>()),
                _provider.GetRequiredService<NodeRegistry>(),
                new EditorViewModel(_provider.GetRequiredService<ILogger<EditorViewModel>>(), _provider.GetRequiredService<NodeRegistry>()),
                Dialogs,
                _provider);
            Window = new MainWindow { DataContext = ViewModel };
            if (window is { } size)
            {
                Window.Width = size.Width;
                Window.Height = size.Height;
            }

            Window.Show();

            // Recent-pipeline tracking waits on this; without it a save never completes.
            Task initialized = ViewModel.InitializeAsync();
            RenderUntil(() => initialized.IsCompleted, "settings initialised");
            initialized.GetAwaiter().GetResult();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Where frames are written. The first read in a test process clears the folder,
    /// so a frame or manifest from an earlier run is never mistaken for this run's.
    /// </summary>
    public static string FrameDirectory => ClearedFrameDirectory.Value;

    public MainWindow Window { get; }

    public MainWindowViewModel ViewModel { get; }

    public StubDialogService Dialogs { get; }

    public string SettingsPath { get; }

    public TempDirectory Temp { get; }

    public static VisualSession Open(ITestOutputHelper output, Size? window = null, ILoggerProvider? loggerProvider = null) =>
        new(output, window, loggerProvider);

    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    public void RenderUntil(Func<bool> condition, string what) => RenderUntil(condition, DefaultDeadline, what);

    public void RenderUntil(Func<bool> condition, TimeSpan deadline, string what)
    {
        var stopwatch = Stopwatch.StartNew();
        int iterations = 0;
        while (!condition())
        {
            if (stopwatch.Elapsed > deadline)
            {
                throw new XunitException(
                    $"Not reached within {deadline.TotalMilliseconds} ms: {what} " +
                    $"({stopwatch.ElapsedMilliseconds} ms, {iterations} iterations).");
            }

            Pump();
            iterations++;
        }

        _output.WriteLine($"[render-until] {what}: {iterations} iterations, {stopwatch.ElapsedMilliseconds} ms");
    }

    public WriteableBitmap Capture(string frameId) => Capture(frameId, Window);

    public WriteableBitmap Capture(string frameId, TopLevel source)
    {
        if (!VisualFrames.IsDeclared(frameId))
        {
            throw new XunitException($"Frame id '{frameId}' is not declared in {nameof(VisualFrames)}.");
        }

        string directory = FrameDirectory;
        lock (WrittenFramesLock)
        {
            if (!WrittenFrames.Add(frameId))
            {
                throw new XunitException($"Frame '{frameId}' was already written in this test process.");
            }
        }

        var stopwatch = Stopwatch.StartNew();
        Pump();
        WriteableBitmap frame = source.CaptureRenderedFrame()
            ?? throw new XunitException($"Frame '{frameId}': nothing was rendered.");
        frame.Save(Path.Combine(directory, frameId + ".png"));
        _output.WriteLine($"[frame] {frameId} {frame.PixelSize.Width}x{frame.PixelSize.Height} {stopwatch.ElapsedMilliseconds} ms");
        return frame;
    }

    public Color MeanColour(WriteableBitmap frame, PixelRect band)
    {
        long a = 0, r = 0, g = 0, b = 0;
        ForEachPixel(frame, band, pixel =>
        {
            a += pixel.A;
            r += pixel.R;
            g += pixel.G;
            b += pixel.B;
        });
        long count = (long)band.Width * band.Height;
        return Color.FromArgb((byte)(a / count), (byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    public List<Color> Pixels(WriteableBitmap frame, PixelRect band)
    {
        var pixels = new List<Color>(band.Width * band.Height);
        ForEachPixel(frame, band, pixels.Add);
        return pixels;
    }

    public int CountDistinctColours(WriteableBitmap frame)
    {
        var colours = new HashSet<uint>();
        ForEachPixel(frame, new PixelRect(frame.PixelSize), pixel => colours.Add(pixel.ToUInt32()));
        return colours.Count;
    }

    // Headless rendering runs at scale 1, so these are also the pixel bounds in a captured frame.
    public PixelRect BoundsInWindow(Avalonia.Visual visual)
    {
        TopLevel topLevel = TopLevel.GetTopLevel(visual)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} is not in a window.");
        Matrix transform = visual.TransformToVisual(topLevel)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");
        return PixelRect.FromRect(new Rect(visual.Bounds.Size).TransformToAABB(transform), topLevel.RenderScaling);
    }

    // Prints the control's bounds against the window's client area; true when it is shown and lies fully inside.
    public bool InsideClient(string name, Avalonia.Visual visual)
    {
        PixelRect bounds = BoundsInWindow(visual);
        var client = new PixelRect(PixelSize.FromSize(Window.ClientSize, Window.RenderScaling));
        bool inside = visual.IsEffectivelyVisible && client.Contains(bounds);
        _output.WriteLine($"[bounds] {name}={bounds} client={client} visible={visual.IsEffectivelyVisible} inside={inside}");
        return inside;
    }

    // Each shown TextBlock under the container whose laid-out text is wider than its own box, or whose
    // box reaches past the sides of the pane: text the pane would cut off. Each one is printed.
    public List<string> TextCutOffBy(Control container, PixelRect pane)
    {
        var cut = new List<string>();
        foreach (TextBlock text in container.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible))
        {
            PixelRect bounds = BoundsInWindow(text);
            double width = text.TextLayout.Width;
            if (width > text.Bounds.Width + 0.5 || bounds.X < pane.X || bounds.Right > pane.Right)
            {
                cut.Add($"'{text.Text}' at {bounds} lays out {width:F1} px wide in a {text.Bounds.Width:F1} px box");
            }
        }

        _output.WriteLine($"[text] pane={pane} cut-off={cut.Count}");
        cut.ForEach(_output.WriteLine);
        return cut;
    }

    // IGNITE (the toolbar's one button bound to the run command) and the shortcuts button,
    // each printed; returns the names of those not shown fully inside the client area.
    public List<string> RunControlsOutsideClient()
    {
        Button ignite = Window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, ViewModel.RunCommand));
        var outside = new List<string>();
        if (!InsideClient("IGNITE", ignite))
        {
            outside.Add("IGNITE");
        }

        if (!InsideClient("Keyboard Shortcuts", ButtonNamed("Keyboard Shortcuts")))
        {
            outside.Add("Keyboard Shortcuts");
        }

        return outside;
    }

    // The visual's centre in the coordinates of its own top level (the window, or a popup's root).
    public Point CentreOf(Avalonia.Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), TopLevel.GetTopLevel(visual)!)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} has no transform to its window.");

    // A left press and release at the visual's centre, sent to the top level that shows it.
    public void Click(Avalonia.Visual visual)
    {
        TopLevel topLevel = TopLevel.GetTopLevel(visual)
            ?? throw new InvalidOperationException($"{visual.GetType().Name} is not in a window.");
        Point centre = CentreOf(visual);
        topLevel.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        topLevel.MouseUp(centre, MouseButton.Left, RawInputModifiers.None);
    }

    // Presses and releases a key gesture, written as the shortcuts dialog writes it ("Ctrl+Shift+S"),
    // on the main window: the focused control receives it first, then the window.
    public void Press(string gesture)
    {
        var parsed = KeyGesture.Parse(gesture);

        // Both flag sets give Alt, Control, Shift and Meta the same bits.
        var modifiers = (RawInputModifiers)parsed.KeyModifiers;
        PhysicalKey physical = Enum.TryParse(parsed.Key.ToString(), out PhysicalKey named) ? named : PhysicalKey.None;
        Window.KeyPress(parsed.Key, modifiers, physical, keySymbol: null);
        Window.KeyRelease(parsed.Key, modifiers, physical, keySymbol: null);
    }

    // The one control of type T in the window that carries this automation id.
    public T ById<T>(string automationId)
        where T : Control =>
        Window.GetVisualDescendants().OfType<T>()
            .Single(control => string.Equals(AutomationProperties.GetAutomationId(control), automationId, StringComparison.Ordinal));

    // The one button in the window that a screen reader announces by this name.
    public Button ButtonNamed(string name) =>
        Window.GetVisualDescendants().OfType<Button>()
            .Single(button => string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal));

    // The text a control shows: its effectively visible TextBlocks, in tree order.
    public List<string> ShownText(Control container) =>
        container.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Text ?? string.Empty)
            .ToList();

    public List<ItemContainer> Stations() =>
        Window.GetVisualDescendants().OfType<ItemContainer>()
            .Where(c => c.DataContext is PipelineNodeViewModel && c.Bounds.Width > 0)
            .ToList();

    public ItemContainer WaitForStation(PipelineNodeViewModel node)
    {
        ItemContainer? Find() => Stations().SingleOrDefault(c => ReferenceEquals(c.DataContext, node));
        RenderUntil(() => Find() is not null, $"{node.TypeKey} station shown");
        return Find()!;
    }

    public Nodify.Avalonia.Connections.Connector WaitForPort(PipelineConnectorViewModel port)
    {
        Nodify.Avalonia.Connections.Connector? Find() => Window.GetVisualDescendants().OfType<Nodify.Avalonia.Connections.Connector>()
            .SingleOrDefault(c => ReferenceEquals(c.DataContext, port) && c.Bounds.Width > 0);
        RenderUntil(() => Find() is not null, $"{port.Node.TypeKey} {(port.IsInput ? "input" : "output")} port shown");
        return Find()!;
    }

    public void LoadTemplate(string templateId)
    {
        ViewModel.LoadTemplateCommand.Execute(templateId);
        RenderUntil(() => Stations().Count == ViewModel.Editor.Nodes.Count, "template stations laid out");
    }

    // The empty canvas's template card buttons, in display order.
    public List<Button> TemplateCards() =>
        Window.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("templateButton") && button.Bounds.Width > 0)
            .ToList();

    // Prints each template card's bounds and the canvas pane's, before and after scrolling the card into view
    // when bringIntoView is set; returns the cards that reach past the pane's sides as laid out, or that do not
    // lie inside the pane after that step.
    public List<string> TemplateCardsOutsideCanvas(bool bringIntoView)
    {
        PixelRect pane = BoundsInWindow(Window.GetVisualDescendants().OfType<FlowForge.UI.Views.CanvasView>().Single());
        List<Button> cards = TemplateCards();
        _output.WriteLine($"[cards] client={Window.ClientSize} pane={pane} checked={cards.Count} bringIntoView={bringIntoView}");
        var outside = new List<string>();
        foreach (Button card in cards)
        {
            PixelRect before = BoundsInWindow(card);
            if (bringIntoView)
            {
                card.BringIntoView();
                Window.UpdateLayout();
            }

            PixelRect after = BoundsInWindow(card);
            _output.WriteLine($"[cards] {card.Tag}: before={before} after={after}");
            if (before.X < pane.X || before.Right > pane.Right)
            {
                outside.Add($"{card.Tag} at {before} reaches past the sides of the canvas pane {pane}");
            }

            if (!pane.Contains(after))
            {
                outside.Add($"{card.Tag} at {after} is outside the canvas pane {pane}");
            }
        }

        return outside;
    }

    public List<ContentPresenter> InspectorCards() =>
        Window.GetVisualDescendants().OfType<ItemsControl>()
            .Single(list => ReferenceEquals(list.ItemsSource, ViewModel.Properties.Fields))
            .GetRealizedContainers().OfType<ContentPresenter>().ToList();

    // Every current field has a laid-out card in the inspector, in field order.
    public bool InspectorReady()
    {
        List<ContentPresenter> cards = InspectorCards();
        return cards.Count > 0
            && cards.Select(card => card.DataContext).SequenceEqual(ViewModel.Properties.Fields)
            && cards.All(card => card.Child is { Bounds.Width: > 0 });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // Owned and fixture windows first, the main window last.
            var closing = OpenWindows.OrderBy(w => ReferenceEquals(w, Window)).ToList();
            _output.WriteLine($"closing-windows={closing.Count}");
            foreach (Window open in closing)
            {
                open.Close();
            }

            RenderUntil(() => OpenWindows.Count == 0, "every window closed");
            _output.WriteLine($"open-windows={OpenWindows.Count}");
        }
        finally
        {
            _provider?.Dispose();
            if (_configHomeSet)
            {
                Environment.SetEnvironmentVariable(ConfigHomeVariable, _previousConfigHome);
            }

            Temp.Dispose();
        }
    }

    private static string ClearFrameDirectory()
    {
        string directory = RepoRoot.Combine("TestResults", "visual");
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    // Reads the band row by row through the locked framebuffer; the test project allows no unsafe code.
    private static void ForEachPixel(WriteableBitmap frame, PixelRect band, Action<Color> visit)
    {
        using ILockedFramebuffer buffer = frame.Lock();
        bool rgba = buffer.Format == PixelFormat.Rgba8888;
        if (!rgba && buffer.Format != PixelFormat.Bgra8888)
        {
            throw new NotSupportedException($"Frame pixel format {buffer.Format} is neither Rgba8888 nor Bgra8888.");
        }

        if (band.Width <= 0 || band.Height <= 0 || !new PixelRect(buffer.Size).Contains(band))
        {
            throw new ArgumentOutOfRangeException(nameof(band), band, $"The band must be a non-empty part of the {buffer.Size} frame.");
        }

        byte[] row = new byte[band.Width * 4];
        for (int y = band.Y; y < band.Bottom; y++)
        {
            Marshal.Copy(buffer.Address + (y * buffer.RowBytes) + (band.X * 4), row, 0, row.Length);
            for (int i = 0; i < row.Length; i += 4)
            {
                visit(rgba
                    ? Color.FromArgb(row[i + 3], row[i], row[i + 1], row[i + 2])
                    : Color.FromArgb(row[i + 3], row[i + 2], row[i + 1], row[i]));
            }
        }
    }
}
