using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using FlowForge.UI.Views;
using FluentAssertions;
using Nodify.Avalonia;
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

    // WCAG AA for body-size text.
    private const double MinimumContrast = 4.5;

    // A subtitle pixel this far (RGB distance) from the body behind it is glyph ink.
    private const double GlyphDistance = 40;

    // The share of glyph ink, farthest from the body first, that a subtitle's contrast is read from.
    private const double KeptInkShare = 0.25;

    // How far inside a heat-pulse phase a capture starts, so the render step it takes stays inside.
    private const double PhaseMargin = 0.05;

    // A string literal handed to a FontFamily in C#: assigned, parsed or constructed from.
    private static readonly Regex LiteralFontFamilyInCode = new(
        @"\bFontFamily\s*(=|\.Parse\s*\()\s*\$?@?""|\bnew\s+FontFamily\s*\(\s*\$?@?""",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    // The heat pulse's opacity normalised to its keyframe range: below 0.35, above 0.65, and between.
    private static readonly (string Frame, string Phase, double From, double To)[] HeatPulsePhases =
    [
        (VisualFrames.RunningSubtitlesDim, "dim", double.NegativeInfinity, 0.35),
        (VisualFrames.RunningSubtitlesBright, "bright", 0.65, double.PositiveInfinity),
        (VisualFrames.RunningSubtitlesMid, "mid", 0.35, 0.65),
    ];

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

    // An Int field holding text it refused is outlined in the error colour, while the user types in it and
    // after focus moves on; a field holding a valid value is not, focused or not.
    [AvaloniaFact]
    public void RefusedIntField_BorderPaintsErrorColour()
    {
        using var session = VisualSession.Open(_output);
        EditorViewModel editor = session.ViewModel.Editor;
        editor.AddNode("ImageResize", new Point(200, 160), session.ViewModel.Registry);
        PipelineNodeViewModel node = editor.Nodes.Single();
        session.Click(session.WaitForStation(node));
        session.RenderUntil(
            () => ReferenceEquals(editor.SelectedNode, node) && session.InspectorReady(),
            "ImageResize selected and its fields shown");
        TextBox Field(string key) => session.InspectorCards()
            .Single(card => card.DataContext is ConfigFieldViewModel field && string.Equals(field.Key, key, StringComparison.Ordinal))
            .GetVisualDescendants().OfType<TextBox>().Single();
        TextBox width = Field("width");
        TextBox height = Field("height");
        Color error = ThemeResource<ISolidColorBrush>("ForgeError").Color;
        var measured = new List<(string Case, bool Refused, Color Colour)>();
        void Measure(WriteableBitmap frame, string state, TextBox box, bool refused)
        {
            Border border = box.GetVisualDescendants().OfType<Border>()
                .Single(part => string.Equals(part.Name, "PART_BorderElement", StringComparison.Ordinal));
            PixelRect bounds = session.BoundsInWindow(border);

            // The box's left edge, its thickest side, clear of the corners.
            var band = new PixelRect(bounds.X, bounds.Y + 2, 1, bounds.Height - 4);
            Color colour = session.MeanColour(frame, band);
            _output.WriteLine($"{state}: text='{box.Text}' focused={box.IsFocused} error-class={box.Classes.Contains("error")} band={band} measured={colour} error={error} tolerance={ColourTolerance}");
            measured.Add((state, refused, colour));
        }

        session.Click(width);
        session.RenderUntil(() => width.IsFocused, "the width field focused");
        session.Window.KeyTextInput("12x");
        session.RenderUntil(() => ((ConfigFieldViewModel)width.DataContext!).HasError, "the width field refuses its text");
        using (WriteableBitmap frame = session.Capture(VisualFrames.IntFieldRefusedFocused))
        {
            Measure(frame, "refused width, focused", width, refused: true);
            Measure(frame, "valid height, unfocused", height, refused: false);
        }

        CheckRefusalIsSaidInWords(session, width);

        session.Click(height);
        session.RenderUntil(() => height.IsFocused && !width.IsFocused, "the height field focused");
        using (WriteableBitmap frame = session.Capture(VisualFrames.IntFieldRefusedUnfocused))
        {
            Measure(frame, "refused width, unfocused", width, refused: true);
            Measure(frame, "valid height, focused", height, refused: false);
        }

        foreach ((string state, bool refused, Color colour) in measured)
        {
            WithinTolerance(colour, error).Should().Be(refused, $"the {state} box is outlined in ForgeError only when it holds refused text");
        }
    }

    // A refused Int field says so in words too: under the box, inside its card, and as the box's help text.
    private void CheckRefusalIsSaidInWords(VisualSession session, TextBox box)
    {
        ContentPresenter card = session.InspectorCards().Single(c => ReferenceEquals(c.DataContext, box.DataContext));
        string? errorText = ((ConfigFieldViewModel)card.DataContext!).ErrorText;
        TextBlock? message = card.GetVisualDescendants().OfType<TextBlock>()
            .SingleOrDefault(block => block.IsEffectivelyVisible && errorText is not null && string.Equals(block.Text, errorText, StringComparison.Ordinal));
        PixelRect cardBounds = session.BoundsInWindow(card);
        PixelRect boxBounds = session.BoundsInWindow(box);
        PixelRect messageBounds = message is null ? default : session.BoundsInWindow(message);
        string? helpText = Avalonia.Automation.AutomationProperties.GetHelpText(box);
        _output.WriteLine($"error-text='{errorText}' message='{message?.Text}' help='{helpText}' card={cardBounds} box={boxBounds} message-bounds={messageBounds}");
        message.Should().NotBeNull("a refused number is said in words, not by colour alone");
        (messageBounds.Width > 0 && messageBounds.Height > 0).Should().BeTrue("the message is laid out");
        cardBounds.Contains(messageBounds).Should().BeTrue("the message sits inside the field's card in the inspector");
        messageBounds.Y.Should().BeGreaterThanOrEqualTo(boxBounds.Bottom, "the message sits under the box");
        helpText.Should().Be(errorText, "a screen reader hears the same message");
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

    [AvaloniaFact]
    public void TightTextBlocks_DoNotClip()
    {
        List<(string Site, bool Tight)> sites = TightSourceSites();
        sites.ForEach(site => _output.WriteLine($"source {site.Site} tight={site.Tight}"));
        _output.WriteLine($"source sites={sites.Count} tight={sites.Count(site => site.Tight)}");

        using var session = VisualSession.Open(_output);
        var states = new List<(string State, int Found, int Compliant, string Clipped)>();
        InEveryState(session, state => states.Add(TightTextIn(session, state)));

        foreach ((string state, int found, int compliant, string clipped) in states)
        {
            _output.WriteLine($"state={state} found={found} compliant={compliant} clipped=[{clipped}]");
        }

        sites.Should().NotBeEmpty("the scan reads the UI's AXAML files");
        sites.Where(site => !site.Tight).Should().BeEmpty("text with a line box no taller than its font carries the tight class");
        foreach ((string state, int found, int compliant, string clipped) in states)
        {
            found.Should().BePositive($"the {state} window shows tight-leaded text");
            compliant.Should().Be(found, $"no tight-leaded text is clipped in the {state} window ({clipped})");
        }
    }

    [AvaloniaFact]
    public void EveryTextBlock_HasBundledGlyphs()
    {
        List<string> bundled = BundledFamilies(_output);

        using var session = VisualSession.Open(_output);
        var walks = new List<(string State, int TextBlocks, int Characters, List<string> Misses)>();
        InEveryState(session, state => walks.Add(GlyphsIn(session.Window, state, bundled)));

        Window window = session.Window;
        window.KeyPress(Key.F1, RawInputModifiers.None, PhysicalKey.F1, keySymbol: null);
        window.KeyRelease(Key.F1, RawInputModifiers.None, PhysicalKey.F1, keySymbol: null);
        session.RenderUntil(
            () => window.OwnedWindows.OfType<ShortcutsWindow>().Any(dialog => dialog.IsVisible && dialog.Bounds.Width > 0),
            "shortcuts dialog shown");
        ShortcutsWindow shortcuts = window.OwnedWindows.OfType<ShortcutsWindow>().Single();
        try
        {
            walks.Add(GlyphsIn(shortcuts, "shortcuts", bundled));
        }
        finally
        {
            shortcuts.Close();
            session.RenderUntil(() => window.OwnedWindows.Count == 0, "shortcuts dialog closed");
        }

        foreach ((string state, int textBlocks, int characters, List<string> misses) in walks)
        {
            _output.WriteLine($"state={state} textblocks={textBlocks} characters={characters} misses={misses.Count}");
            misses.ForEach(miss => _output.WriteLine($"  miss {miss}"));
        }

        bundled.Should().Contain("Inter", "the body font is the bundled Inter");
        foreach ((string state, int textBlocks, int characters, List<string> misses) in walks)
        {
            textBlocks.Should().BePositive($"the {state} window shows text");
            misses.Should().BeEmpty($"every text in the {state} window is drawn by a bundled font that has each of its characters");
        }
    }

    [AvaloniaFact]
    public void UiSources_NameNoLiteralFontFamily()
    {
        string uiRoot = RepoRoot.Combine("src", "FlowForge.UI");
        var files = UiSources(uiRoot, "*.axaml").Concat(UiSources(uiRoot, "*.cs")).ToList();
        var hits = files.SelectMany(relative => LiteralFontFamilies(uiRoot, relative)).ToList();

        hits.ForEach(hit => _output.WriteLine($"hit {hit}"));
        _output.WriteLine($"files scanned={files.Count} hits={hits.Count}");
        files.Should().NotBeEmpty("the scan reads the UI's sources");
        hits.Should().BeEmpty("the UI names fonts only through the theme's bundled font resources");
    }

    [AvaloniaFact]
    public void RunningSubtitle_MeetsContrastInEveryCategory()
    {
        using var session = VisualSession.Open(_output);
        MainWindowViewModel vm = session.ViewModel;
        EditorViewModel editor = vm.Editor;

        // One station per category: the first registered type of each.
        var typeKeys = vm.Registry.GetRegisteredTypeKeys()
            .GroupBy(key => MwOpsMap.Get(key).Category, StringComparer.Ordinal)
            .Select(category => category.First())
            .ToList();
        for (int i = 0; i < typeKeys.Count; i++)
        {
            editor.AddNode(typeKeys[i], new Point(40 + ((i % 3) * 260), 40 + ((i / 3) * 190)), vm.Registry);
        }

        // Fitting measures the laid-out stations, so they must exist first.
        session.RenderUntil(() => session.Stations().Count == typeKeys.Count, "one station per category laid out");
        editor.RequestFitToScreen();
        NodifyEditor canvas = session.Window.GetVisualDescendants().OfType<NodifyEditor>()
            .Single(e => string.Equals(e.Name, "Editor", StringComparison.Ordinal));
        var visible = new Rect(canvas.Bounds.Size);
        session.RenderUntil(
            () => session.Stations().All(station =>
                visible.Contains(station.TranslatePoint(default, canvas)!.Value)
                && visible.Contains(station.TranslatePoint(new Point(station.Bounds.Width, station.Bounds.Height), canvas)!.Value)),
            "every station fitted on screen");

        var ratios = new List<(string Category, double Ratio)>();
        var phases = new List<(string Phase, double From, double To, List<double> Reached)>();
        vm.IsDemoMode = true;
        try
        {
            session.RenderUntil(() => editor.Nodes.All(node => node.IsRunning), "stations running in DEMO mode");
            var pulses = session.Stations()
                .Select(station => station.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("heat-pulse")))
                .ToList();
            (double low, double high) = OpacityKeyFrameRange(pulses[0]);
            double Phase(Border pulse) => (pulse.Opacity - low) / (high - low);
            _output.WriteLine($"heat-pulse opacity keyframes low={low} high={high}");

            foreach ((string frameId, string phase, double from, double to) in HeatPulsePhases)
            {
                session.RenderUntil(
                    () => pulses.All(pulse => Phase(pulse) > from + PhaseMargin && Phase(pulse) < to - PhaseMargin),
                    $"heat pulse {phase}");
                using WriteableBitmap frame = session.Capture(frameId);
                var reached = pulses.Select(Phase).ToList();
                phases.Add((phase, from, to, reached));
                _output.WriteLine($"frame={frameId} phase={phase} from={from} to={to} reached=[{string.Join(", ", reached.Select(t => t.ToString("F2", CultureInfo.InvariantCulture)))}]");
                foreach ((string category, double ratio, string line) in SubtitleContrasts(session, frame))
                {
                    ratios.Add((category, ratio));
                    _output.WriteLine($"  {line}");
                }
            }
        }
        finally
        {
            // Every running station animates for as long as DEMO mode is on; stop it before the window closes.
            vm.IsDemoMode = false;
        }

        var worst = ratios.GroupBy(sample => sample.Category, StringComparer.Ordinal)
            .Select(category => (Category: category.Key, Captures: category.Count(), Worst: category.Min(sample => sample.Ratio)))
            .ToList();
        worst.ForEach(category => _output.WriteLine($"category={category.Category} captures={category.Captures} worst={category.Worst:F2} minimum={MinimumContrast}"));

        foreach ((string phase, double from, double to, List<double> reached) in phases)
        {
            reached.Should().OnlyContain(t => t > from && t < to, $"the {phase} frame is captured inside its heat-pulse phase");
        }

        worst.Select(category => category.Category).Should().BeEquivalentTo(["src", "snk", "shp", "flt", "hea", "met"]);
        foreach ((string category, int captures, double ratio) in worst)
        {
            captures.Should().Be(HeatPulsePhases.Length, $"every frame measures the {category} subtitle");
            ratio.Should().BeGreaterThanOrEqualTo(MinimumContrast, $"the running {category} subtitle stands out from its station in every heat-pulse phase");
        }
    }

    // Empty, a template loaded, running, and DEMO mode: the states the text checks walk.
    private static void InEveryState(VisualSession session, Action<string> visit)
    {
        MainWindowViewModel vm = session.ViewModel;
        visit("empty");
        session.LoadTemplate(VisualSession.WebExportTemplate);
        visit("template-loaded");

        vm.ExecutionLog.IsRunning = true;
        try
        {
            session.RenderUntil(() => vm.Editor.Nodes.All(node => node.IsRunning), "stations running");
            visit("running");
        }
        finally
        {
            vm.ExecutionLog.IsRunning = false;
        }

        vm.IsDemoMode = true;
        try
        {
            session.RenderUntil(() => vm.Editor.Nodes.All(node => node.IsRunning), "stations running in DEMO mode");
            visit("demo");
        }
        finally
        {
            // The beads animate for as long as DEMO mode is on; stop it before the window closes.
            vm.IsDemoMode = false;
        }
    }

    // Text whose line box is no taller than its font: how many the window holds, how many
    // draw past their bounds, and the text of those that do not.
    private static (string State, int Found, int Compliant, string Clipped) TightTextIn(VisualSession session, string state)
    {
        var tight = session.Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => !double.IsNaN(block.LineHeight) && block.LineHeight <= block.FontSize)
            .ToList();
        string clipped = string.Join(", ", tight.Where(block => block.ClipToBounds).Select(block => $"'{block.Text}'"));
        return (state, tight.Count, tight.Count(block => !block.ClipToBounds), clipped);
    }

    // Every TextBlock with text under the root, hidden ones included: the typeface its font
    // resolves to must be a bundled family and hold a glyph for each character it shows.
    private static (string State, int TextBlocks, int Characters, List<string> Misses) GlyphsIn(
        Avalonia.Visual root, string state, List<string> bundled)
    {
        var blocks = root.GetVisualDescendants().OfType<TextBlock>().Where(block => !string.IsNullOrEmpty(block.Text)).ToList();
        int characters = 0;
        var misses = new List<string>();
        foreach (TextBlock block in blocks)
        {
            var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
            string family = ResolvedFamily(typeface);
            if (!bundled.Contains(family, StringComparer.Ordinal))
            {
                misses.Add($"'{block.Text}' font='{block.FontFamily}' resolves={family} (not bundled)");
                continue;
            }

            FontManager.Current.TryGetGlyphTypeface(typeface, out IGlyphTypeface? glyphs);
            foreach (Rune rune in block.Text!.EnumerateRunes().Where(rune => !Rune.IsControl(rune)))
            {
                characters++;
                if (!glyphs!.TryGetGlyph((uint)rune.Value, out ushort glyph) || glyph == 0)
                {
                    misses.Add($"'{rune}' U+{rune.Value:X4} in '{block.Text}' resolves={family} (no glyph)");
                }
            }
        }

        return (state, blocks.Count, characters, misses);
    }

    // The families of the fonts the app ships: its own embedded fonts and the Inter package's,
    // read from the font assets themselves (Inter's heavier weights name their own family).
    internal static List<string> BundledFamilies(ITestOutputHelper output) =>
        FamiliesIn(output, new Uri("avares://FlowForge.UI/Assets/Fonts"))
            .Concat(FamiliesIn(output, new Uri("avares://Avalonia.Fonts.Inter/Assets")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    // A font collection under its own key reads the folder's assets; it is removed again so
    // the app's font lookup is left as it was.
    private static List<string> FamiliesIn(ITestOutputHelper output, Uri folder)
    {
        var assets = AssetLoader.GetAssets(folder, null).Select(asset => asset.AbsolutePath).Order(StringComparer.Ordinal).ToList();
        var key = new Uri("fonts:BundledFontCheck");
        var collection = new EmbeddedFontCollection(key, folder);
        FontManager.Current.AddFontCollection(collection);
        try
        {
            var families = collection.Select(family => family.Name).Order(StringComparer.Ordinal).ToList();
            output.WriteLine($"folder={folder} assets=[{string.Join(", ", assets)}] families=[{string.Join(", ", families)}]");
            families.Should().NotBeEmpty($"{folder} holds the fonts the app ships");
            return families;
        }
        finally
        {
            FontManager.Current.RemoveFontCollection(key);
        }
    }

    // Every TextBlock in the UI's AXAML whose LineHeight is no taller than its FontSize,
    // as "file:line", and whether it carries the tight class.
    private static List<(string Site, bool Tight)> TightSourceSites()
    {
        string uiRoot = RepoRoot.Combine("src", "FlowForge.UI");
        return UiSources(uiRoot, "*.axaml")
            .SelectMany(relative => XDocument.Load(Path.Combine(uiRoot, relative), LoadOptions.SetLineInfo).Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "TextBlock", StringComparison.Ordinal)
                    && Number(element, "LineHeight") is double lineHeight
                    && Number(element, "FontSize") is double fontSize
                    && lineHeight <= fontSize)
                .Select(element => (
                    Site(relative, element),
                    ((string?)element.Attribute("Classes") ?? string.Empty)
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("tight", StringComparer.Ordinal))))
            .ToList();
    }

    // The UI project's source files matching the pattern, relative to its folder, build output skipped.
    private static List<string> UiSources(string uiRoot, string pattern) =>
        Directory.EnumerateFiles(uiRoot, pattern, SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(uiRoot, file))
            .Where(relative => !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .ToList();

    // A font named by its family rather than through a resource: in AXAML a FontFamily
    // attribute, setter or property element whose value is not a markup extension, or a
    // FontFamily element that is not a keyed resource; in C# a string handed to a FontFamily.
    private static IEnumerable<string> LiteralFontFamilies(string uiRoot, string relative)
    {
        string path = Path.Combine(uiRoot, relative);
        if (string.Equals(Path.GetExtension(relative), ".cs", StringComparison.Ordinal))
        {
            return File.ReadLines(path)
                .Select((text, index) => (Text: text, Line: index + 1))
                .Where(line => LiteralFontFamilyInCode.IsMatch(line.Text))
                .Select(line => $"{relative.Replace('\\', '/')}:{line.Line} {line.Text.Trim()}");
        }

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path, LoadOptions.SetLineInfo).Descendants()
            .SelectMany(element => element.Attributes()
                .Where(attribute => NamesFontFamily(attribute.Name.LocalName) && IsLiteral(attribute.Value))
                .Select(attribute => $"{Site(relative, element)} {attribute.Name.LocalName}=\"{attribute.Value}\"")
                .Concat(string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)
                    && NamesFontFamily((string?)element.Attribute("Property") ?? string.Empty)
                    && element.Attribute("Value") is { } value && IsLiteral(value.Value)
                        ? [$"{Site(relative, element)} Setter Value=\"{value.Value}\""]
                        : [])
                .Concat(NamesFontFamily(element.Name.LocalName)
                    && element.Attribute(xaml + "Key") is null
                    && !element.HasElements && IsLiteral(element.Value)
                        ? [$"{Site(relative, element)} <{element.Name.LocalName}>{element.Value}"]
                        : []));
    }

    private static bool NamesFontFamily(string name) =>
        string.Equals(name, "FontFamily", StringComparison.Ordinal) || name.EndsWith(".FontFamily", StringComparison.Ordinal);

    private static bool IsLiteral(string value) =>
        !string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('{');

    private static string Site(string relative, XElement element) =>
        $"{relative.Replace('\\', '/')}:{((IXmlLineInfo)element).LineNumber}";

    private static double? Number(XElement element, string attribute) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;

    private static string ResolvedFamily(Typeface typeface)
    {
        FontManager.Current.TryGetGlyphTypeface(typeface, out IGlyphTypeface? glyphs).Should().BeTrue($"'{typeface.FontFamily}' resolves to a typeface");
        return glyphs!.FamilyName;
    }

    // The smallest and largest opacity the running pulse's keyframes set, read from its style.
    private static (double Low, double High) OpacityKeyFrameRange(Border pulse)
    {
        var opacities = pulse.Styles.OfType<Style>().Single(style => style.Animations.Count > 0)
            .Animations.OfType<Animation>()
            .SelectMany(animation => animation.Children)
            .SelectMany(keyFrame => keyFrame.Setters.OfType<Setter>())
            .Where(setter => setter.Property == Avalonia.Visual.OpacityProperty)
            .Select(setter => Convert.ToDouble(setter.Value, CultureInfo.InvariantCulture))
            .ToList();
        opacities.Should().NotBeEmpty("the running heat pulse animates its opacity");
        return (opacities.Min(), opacities.Max());
    }

    // Per station, the subtitle's glyph pixels against the station behind them. The heat pulse
    // lights the body unevenly, so what lies behind each pixel of the text area is read from the
    // rows directly above and below the text in that column (body, or a plate where one is drawn),
    // blended by height. Glyph pixels are those far from it. Each is a per-channel blend of the
    // text colour and that body (greyscale antialiasing blends every channel alike, subpixel
    // antialiasing each channel on its own), so for text lighter or darker than its body in every
    // channel no glyph pixel contrasts with the body more than the text colour does. A mean over
    // every glyph pixel takes in the edges, which measure the antialiasing rather than the
    // colours; the farthest quarter are the strokes. The ratio is the WCAG contrast of their mean
    // against the mean of what lies behind them: a lower bound on the text's own contrast under
    // either antialiasing.
    private static List<(string Category, double Ratio, string Line)> SubtitleContrasts(VisualSession session, WriteableBitmap frame)
    {
        var contrasts = new List<(string Category, double Ratio, string Line)>();
        foreach (ItemContainer station in session.Stations())
        {
            var node = (PipelineNodeViewModel)station.DataContext!;
            TextBlock subtitle = station.GetVisualDescendants().OfType<TextBlock>()
                .Single(block => string.Equals(block.Text, node.MwSub, StringComparison.Ordinal));
            PixelRect bounds = session.BoundsInWindow(subtitle);
            double scale = bounds.Height / subtitle.Bounds.Height;
            int width = Math.Min(bounds.Width, (int)Math.Ceiling(subtitle.TextLayout.WidthIncludingTrailingWhitespace * scale));
            var text = new PixelRect(bounds.X, bounds.Y, width, bounds.Height);
            List<Color> above = session.Pixels(frame, new PixelRect(text.X, text.Y - 1, width, 1));
            List<Color> below = session.Pixels(frame, new PixelRect(text.X, text.Bottom, width, 1));
            List<Color> pixels = session.Pixels(frame, text);
            var ink = new List<(Color Pixel, Color Body)>();
            for (int i = 0; i < pixels.Count; i++)
            {
                Color body = Blend(above[i % width], below[i % width], ((i / width) + 1.0) / (text.Height + 1));
                if (Distance(pixels[i], body) >= GlyphDistance)
                {
                    ink.Add((pixels[i], body));
                }
            }

            var kept = ink.OrderByDescending(sample => Distance(sample.Pixel, sample.Body))
                .Take((int)Math.Ceiling(ink.Count * KeptInkShare))
                .ToList();
            Color glyph = kept.Count > 0 ? Mean(kept.ConvertAll(sample => sample.Pixel)) : Mean(above);
            Color back = kept.Count > 0 ? Mean(kept.ConvertAll(sample => sample.Body)) : Mean(above);
            double ratio = ContrastRatio(glyph, back);
            string category = MwOpsMap.Get(node.TypeKey).Category;
            contrasts.Add((category, ratio, $"category={category} type={node.TypeKey} subtitle='{node.MwSub}' foreground={subtitle.Foreground} text={text} above={Mean(above)} below={Mean(below)} glyph-pixels={ink.Count} kept={kept.Count} glyph={glyph} behind={back} ratio={ratio:F2}"));
        }

        return contrasts;
    }

    private static Color Blend(Color from, Color to, double t) =>
        Color.FromRgb(Lerp(from.R, to.R, t), Lerp(from.G, to.G, t), Lerp(from.B, to.B, t));

    private static double Distance(Color a, Color b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

    private static Color Mean(List<Color> pixels) =>
        Color.FromRgb(
            (byte)Math.Round(pixels.Average(pixel => pixel.R)),
            (byte)Math.Round(pixels.Average(pixel => pixel.G)),
            (byte)Math.Round(pixels.Average(pixel => pixel.B)));

    private static double ContrastRatio(Color a, Color b)
    {
        double lighter = Math.Max(RelativeLuminance(a), RelativeLuminance(b));
        double darker = Math.Min(RelativeLuminance(a), RelativeLuminance(b));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color colour) =>
        (0.2126 * Linear(colour.R)) + (0.7152 * Linear(colour.G)) + (0.0722 * Linear(colour.B));

    private static double Linear(byte channel)
    {
        double value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
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
