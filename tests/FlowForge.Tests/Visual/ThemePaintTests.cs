using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The theme paints what it names: the accent button and the controls the base theme draws in
/// its accent colour show the forge palette. Expected colours are read from the live theme.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class ThemePaintTests
{
    // Per channel, on a band's mean colour.
    private const int ColourTolerance = 8;

    private readonly ITestOutputHelper _output;

    public ThemePaintTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void IgniteButton_PaintsThemeBrushInEveryState()
    {
        using var session = VisualSession.Open(_output);
        ExecutionLogViewModel log = session.ViewModel.ExecutionLog;
        string? summaryBefore = log.Summary;
        Button ignite = session.Window.GetVisualDescendants().OfType<ToolbarView>().Single()
            .GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("accent"));
        ContentPresenter presenter = ignite.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(part => string.Equals(part.Name, "PART_ContentPresenter", StringComparison.Ordinal));
        Point centre = session.CentreOf(ignite);
        var samples = new List<(string State, string Key, bool IsThemeBrush, Color Measured, Color Expected)>
        {
            Sample(session, presenter, "default", VisualFrames.IgniteDefault, "MwMoltenButton"),
        };

        session.Window.MouseMove(centre, RawInputModifiers.None);
        session.RenderUntil(() => ignite.IsPointerOver, "pointer over IGNITE");
        samples.Add(Sample(session, presenter, "pointerover", VisualFrames.IgnitePointerOver, "MwMoltenHi"));

        session.Window.MouseDown(centre, MouseButton.Left, RawInputModifiers.None);
        session.RenderUntil(() => ignite.IsPressed, "IGNITE pressed");
        samples.Add(Sample(session, presenter, "pressed", VisualFrames.IgnitePressed, "MwMoltenLo"));

        // Released away from the button, so the press is no click and starts no run.
        Point away = centre + new Vector(0, 120);
        session.Window.MouseMove(away, RawInputModifiers.LeftMouseButton);
        session.Window.MouseUp(away, MouseButton.Left, RawInputModifiers.None);
        session.Pump();

        foreach ((string state, string key, bool isThemeBrush, Color measured, Color expected) in samples)
        {
            _output.WriteLine($"state={state} resource={key} theme-brush={isThemeBrush} tolerance={ColourTolerance} measured={measured} expected={expected}");
        }

        _output.WriteLine($"running={log.IsRunning} summary-before='{summaryBefore}' summary-after='{log.Summary}'");
        foreach ((string state, string key, bool isThemeBrush, Color measured, Color expected) in samples)
        {
            isThemeBrush.Should().BeTrue($"IGNITE's {state} background is the theme's {key}");
            WithinTolerance(measured, expected).Should().BeTrue($"IGNITE's {state} band is painted in {key}");
        }

        log.IsRunning.Should().BeFalse();
        log.Summary.Should().Be(summaryBefore, "a press released off the button starts no run");
    }

    [AvaloniaFact]
    public void AccentCheckBox_PaintsDarkPaletteAccent()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("FolderInput", new Point(200, 160), session.ViewModel.Registry);
        PipelineNodeViewModel node = editor.Nodes.Single();
        session.Click(session.WaitForStation(node));
        session.RenderUntil(
            () => ReferenceEquals(editor.SelectedNode, node) && session.InspectorReady(),
            "FolderInput selected and its fields shown");

        session.Click(RecursiveCheckBox(session));

        // Checking the box commits an edit; run whatever it posts before waiting for the laid-out box.
        session.Pump();
        session.RenderUntil(
            () => session.InspectorReady() && RecursiveCheckBox(session) is { IsChecked: true, Bounds.Width: > 0 },
            "Include Subfolders checked");
        using WriteableBitmap frame = session.Capture(VisualFrames.AccentCheckBox);

        Border box = RecursiveCheckBox(session).GetVisualDescendants().OfType<Border>()
            .Single(border => string.Equals(border.Name, "NormalRectangle", StringComparison.Ordinal));
        PixelRect bounds = session.BoundsInWindow(box);

        // Inside the filled box, left of the check mark.
        var band = new PixelRect(bounds.X + 2, bounds.Y + (bounds.Height / 2) - 1, 2, 2);
        Color measured = session.MeanColour(frame, band);

        // The base theme fills a checked box with a lighter shade it derives from the palette's Accent.
        FluentTheme fluent = Application.Current!.Styles.OfType<FluentTheme>().Single();
        bool hasDarkPalette = fluent.Palettes.TryGetValue(ThemeVariant.Dark, out ColorPaletteResources? dark);
        object? light1 = null;
        dark?.TryGetResource("SystemAccentColorLight1", ThemeVariant.Dark, out light1);
        _output.WriteLine($"box={bounds} band={band} measured={measured} dark-palette={hasDarkPalette} accent={dark?.Accent} accent-light1={light1} tolerance={ColourTolerance}");
        hasDarkPalette.Should().BeTrue("the app sets the base theme's Dark palette");
        Color expected = light1.Should().BeOfType<Color>("the Dark palette derives SystemAccentColorLight1 from its Accent").Subject;
        WithinTolerance(measured, expected).Should().BeTrue("a checked box is filled with the Dark palette's SystemAccentColorLight1");
    }

    [AvaloniaFact]
    public void BodyFont_ResolvesToBundledInter()
    {
        using var session = VisualSession.Open(_output);
        FontFamily body = ThemeResource<FontFamily>("MwBody");
        string bodyFamily = ResolvedFamily(new Typeface(body));

        // Text that takes the window's font rather than a font of its own.
        TextBlock text = session.Window.GetVisualDescendants().OfType<TextBlock>()
            .First(block => !block.IsSet(TextBlock.FontFamilyProperty)
                && ReferenceEquals(block.FontFamily, session.Window.FontFamily)
                && !string.IsNullOrEmpty(block.Text)
                && block.IsEffectivelyVisible);
        string textFamily = ResolvedFamily(new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch));

        _output.WriteLine($"MwBody='{body}' resolves={bodyFamily}");
        _output.WriteLine($"text='{text.Text}' font='{text.FontFamily}' weight={text.FontWeight} resolves={textFamily}");
        bodyFamily.Should().Be("Inter", "the body font is the bundled Inter");
        textFamily.Should().Be("Inter", "window text without its own font uses the body font");
    }

    private static string ResolvedFamily(Typeface typeface)
    {
        FontManager.Current.TryGetGlyphTypeface(typeface, out IGlyphTypeface? glyphs).Should().BeTrue($"'{typeface.FontFamily}' resolves to a typeface");
        return glyphs!.FamilyName;
    }

    // One state's frame: whether the presenter shows the theme's brush, the band's mean colour,
    // and that brush's colour over the band. The band is the left padding, clear of the text,
    // inset past the rounded corners and the antialiased edge.
    private static (string State, string Key, bool IsThemeBrush, Color Measured, Color Expected) Sample(
        VisualSession session, ContentPresenter presenter, string state, string frameId, string key)
    {
        using WriteableBitmap frame = session.Capture(frameId);
        IBrush brush = ThemeResource<IBrush>(key);
        PixelRect bounds = session.BoundsInWindow(presenter);
        var band = new PixelRect(bounds.X + 1, bounds.Y + 3, 6, bounds.Height - 6);
        return (state, key, ReferenceEquals(presenter.Background, brush), session.MeanColour(frame, band), ExpectedMean(brush, bounds, band));
    }

    private static T ThemeResource<T>(string key)
    {
        Application.Current!.TryGetResource(key, ThemeVariant.Dark, out object? value).Should().BeTrue($"the theme defines {key}");
        return value.Should().BeAssignableTo<T>().Subject;
    }

    // A solid brush is its colour. A top-to-bottom gradient is its stops interpolated at the
    // centre of each band row, relative to the painted bounds, then averaged.
    private static Color ExpectedMean(IBrush brush, PixelRect bounds, PixelRect band)
    {
        if (brush is ISolidColorBrush solid)
        {
            return solid.Color;
        }

        ILinearGradientBrush gradient = brush.Should().BeAssignableTo<ILinearGradientBrush>().Subject;
        gradient.StartPoint.Should().Be(new RelativePoint(0, 0, RelativeUnit.Relative));
        gradient.EndPoint.Should().Be(new RelativePoint(0, 1, RelativeUnit.Relative));
        var stops = gradient.GradientStops.OrderBy(stop => stop.Offset).ToList();
        double r = 0, g = 0, b = 0;
        for (int y = band.Y; y < band.Bottom; y++)
        {
            Color colour = GradientAt(stops, (y + 0.5 - bounds.Y) / bounds.Height);
            r += colour.R;
            g += colour.G;
            b += colour.B;
        }

        return Color.FromRgb(
            (byte)Math.Round(r / band.Height),
            (byte)Math.Round(g / band.Height),
            (byte)Math.Round(b / band.Height));
    }

    private static Color GradientAt(List<IGradientStop> stops, double offset)
    {
        if (offset <= stops[0].Offset)
        {
            return stops[0].Color;
        }

        for (int i = 1; i < stops.Count; i++)
        {
            if (offset <= stops[i].Offset)
            {
                IGradientStop from = stops[i - 1];
                IGradientStop to = stops[i];
                double t = (offset - from.Offset) / (to.Offset - from.Offset);
                return Color.FromRgb(Lerp(from.Color.R, to.Color.R, t), Lerp(from.Color.G, to.Color.G, t), Lerp(from.Color.B, to.Color.B, t));
            }
        }

        return stops[^1].Color;
    }

    private static byte Lerp(byte from, byte to, double t) => (byte)Math.Round(from + ((to - from) * t));

    private static CheckBox RecursiveCheckBox(VisualSession session) =>
        session.InspectorCards()
            .Single(card => card.DataContext is ConfigFieldViewModel { Key: "recursive" })
            .GetVisualDescendants().OfType<CheckBox>().Single();

    private static bool WithinTolerance(Color actual, Color expected) =>
        Math.Abs(actual.R - expected.R) <= ColourTolerance
        && Math.Abs(actual.G - expected.G) <= ColourTolerance
        && Math.Abs(actual.B - expected.B) <= ColourTolerance;
}
