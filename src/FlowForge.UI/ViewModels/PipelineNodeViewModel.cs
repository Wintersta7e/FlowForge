using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;

namespace FlowForge.UI.ViewModels;

public partial class PipelineNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    private Point _location;

    [ObservableProperty]
    private bool _isSelected;

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Title { get; }
    public string IconEmoji { get; }
    public string ConfigPreview { get; }
    public NodeCategory Category { get; }
    public ObservableCollection<PipelineConnectorViewModel> Input { get; } = new();
    public ObservableCollection<PipelineConnectorViewModel> Output { get; } = new();
    public IDictionary<string, JsonElement> Config { get; }

    /// <summary>Molten Works short code, e.g. "SHP.01".</summary>
    public string MwCode { get; private set; } = string.Empty;

    /// <summary>Molten Works subtitle, e.g. "folder · input".</summary>
    public string MwSub { get; private set; } = string.Empty;

    /// <summary>Molten Works category bucket (src/snk/shp/flt/hea/met).</summary>
    public string MwCategory { get; private set; } = "hea";

    [ObservableProperty]
    private IBrush _categoryBrush = null!;

    [ObservableProperty]
    private IBrush _headerBrush = null!;

    [ObservableProperty]
    private IBrush _nodeBorderBrush = null!;

    [ObservableProperty]
    private IBrush _nodeBackground = null!;

    /// <summary>Mw icon-well base fill brush.</summary>
    [ObservableProperty]
    private IBrush _mwBaseBrush = null!;

    /// <summary>Mw category accent / neon brush.</summary>
    [ObservableProperty]
    private IBrush _mwNeonBrush = null!;

    /// <summary>Mw outer glow brush (drop-shadow when running).</summary>
    [ObservableProperty]
    private IBrush _mwGlowBrush = null!;

    /// <summary>Mw category glow color for bindable drop-shadow effects.</summary>
    [ObservableProperty]
    private Color _categoryGlowColor;

    /// <summary>Mw running heat-pulse brush, tinted by category glow.</summary>
    [ObservableProperty]
    private IBrush _mwHeatPulseBrush = null!;

    /// <summary>Forge-themed icon geometry (pit/mold/die/chisel/…).</summary>
    [ObservableProperty]
    private Geometry? _mwIconGeometry;

    /// <summary>Whether the station should render in "running" state.</summary>
    [ObservableProperty]
    private bool _isRunning;

    private readonly EventHandler? _themeChangedHandler;

    public PipelineNodeViewModel(NodeDefinition definition, NodeRegistry registry)
    {
        Id = definition.Id;
        TypeKey = definition.TypeKey;
        Title = registry.GetDisplayName(definition.TypeKey);
        Category = registry.GetCategoryForTypeKey(definition.TypeKey);
        Config = definition.Config;
        IconEmoji = NodeIconMap.Icons.GetValueOrDefault(definition.TypeKey, "\u2699");
        ConfigPreview = BuildConfigPreview(definition.Config);
        _location = new Point(definition.Position.X, definition.Position.Y);

        MwOpsMap.OpMeta meta = MwOpsMap.Get(definition.TypeKey);
        MwCode = meta.Code;
        MwSub = meta.Sub;
        MwCategory = meta.Category;

        RebuildBrushes();

        if (Application.Current is { } app)
        {
            _themeChangedHandler = new EventHandler((_, _) => RebuildBrushes());
            app.ActualThemeVariantChanged += _themeChangedHandler;
        }

        // Source nodes have no input; output nodes have no output
        if (Category != NodeCategory.Source)
        {
            Input.Add(new PipelineConnectorViewModel("In", isInput: true, this));
        }

        if (Category != NodeCategory.Output)
        {
            Output.Add(new PipelineConnectorViewModel("Out", isInput: false, this));
        }
    }

    public void Detach()
    {
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= _themeChangedHandler;
        }
    }

    private void RebuildBrushes()
    {
        Color categoryColor = Category switch
        {
            NodeCategory.Source => ThemeHelper.GetColor("ForgeSourceColor", "#5bb8f5"),
            NodeCategory.Transform => ThemeHelper.GetColor("ForgeTransformColor", "#5ce0a0"),
            NodeCategory.Output => ThemeHelper.GetColor("ForgeOutputColor", "#e8932f"),
            _ => ThemeHelper.GetColor("ForgeTransformColor", "#5ce0a0")
        };

        Color surfaceColor = ThemeHelper.GetColor("ForgeSurfaceColor", "#1c1820");

        CategoryBrush = Category switch
        {
            NodeCategory.Source => ThemeHelper.GetBrush("ForgeSource", "#5bb8f5"),
            NodeCategory.Transform => ThemeHelper.GetBrush("ForgeTransform", "#5ce0a0"),
            NodeCategory.Output => ThemeHelper.GetBrush("ForgeOutput", "#e8932f"),
            _ => ThemeHelper.GetBrush("ForgeTransform", "#5ce0a0")
        };

        // Header gradient: ~5% opacity category color → transparent
        byte headerAlpha = (byte)(Category == NodeCategory.Output ? 0x0E : 0x0D);
        HeaderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(headerAlpha, categoryColor.R, categoryColor.G, categoryColor.B), 0),
                new GradientStop(Colors.Transparent, 1)
            }
        };

        // Border: ~20% opacity
        NodeBorderBrush = new SolidColorBrush(Color.FromArgb(0x33, categoryColor.R, categoryColor.G, categoryColor.B));

        // Node background: subtle category tint → surface
        byte bgAlpha = (byte)(Category == NodeCategory.Output ? 0x14 : 0x0F);
        NodeBackground = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(bgAlpha, categoryColor.R, categoryColor.G, categoryColor.B), 0),
                new GradientStop(surfaceColor, 0.5)
            }
        };

        MwBaseBrush = ThemeHelper.GetBrush(MwOpsMap.BaseKey(MwCategory), "#3a1808");
        MwNeonBrush = ThemeHelper.GetBrush(MwOpsMap.NeonKey(MwCategory), "#ff7a1a");
        MwGlowBrush = ThemeHelper.GetBrush(MwOpsMap.GlowKey(MwCategory), "#ffd080");
        Color glowColor = ThemeHelper.GetColor(MwGlowColorKey(MwCategory), "#ffd080");
        CategoryGlowColor = glowColor;
        MwHeatPulseBrush = BuildHeatPulseBrush(glowColor);
        MwIconGeometry = ThemeHelper.GetGeometry(MwOpsMap.IconKey(MwOpsMap.Get(TypeKey).Icon));
    }

    private static IBrush BuildHeatPulseBrush(Color glowColor)
    {
        return new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.7, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xA6, glowColor.R, glowColor.G, glowColor.B), 0),
                new GradientStop(Color.FromArgb(0x40, glowColor.R, glowColor.G, glowColor.B), 0.45),
                new GradientStop(Colors.Transparent, 0.65),
            },
        };
    }

    private static string MwGlowColorKey(string category) => category switch
    {
        "src" => "MwSrcGlowColor",
        "snk" => "MwSnkGlowColor",
        "shp" => "MwShpGlowColor",
        "flt" => "MwFltGlowColor",
        "hea" => "MwHeaGlowColor",
        "met" => "MwMetGlowColor",
        _ => "MwMoltenHiColor",
    };

    private static string BuildConfigPreview(IDictionary<string, JsonElement> config)
    {
        // Show the first string config value as a preview (path, pattern, etc.)
        foreach (KeyValuePair<string, JsonElement> kvp in config)
        {
            if (kvp.Value.ValueKind == JsonValueKind.String)
            {
                string val = kvp.Value.GetString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        return string.Empty;
    }
}
