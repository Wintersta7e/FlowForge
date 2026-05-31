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
    private readonly MwOpsMap.OpMeta _meta;

    [ObservableProperty]
    private Point _location;

    [ObservableProperty]
    private bool _isSelected;

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Title { get; }
    public string ConfigPreview { get; }
    public NodeCategory Category { get; }
    public ObservableCollection<PipelineConnectorViewModel> Input { get; } = new();
    public ObservableCollection<PipelineConnectorViewModel> Output { get; } = new();
    public IDictionary<string, JsonElement> Config { get; }

    /// <summary>Molten Works short code, e.g. "SHP.01".</summary>
    public string MwCode => _meta.Code;

    /// <summary>Molten Works subtitle, e.g. "folder · input".</summary>
    public string MwSub => _meta.Sub;

    public IBrush CategoryBrush { get; }
    public IBrush HeaderBrush { get; }
    public IBrush NodeBorderBrush { get; }
    public IBrush NodeBackground { get; }

    /// <summary>Mw icon-well base fill brush.</summary>
    public IBrush MwBaseBrush { get; }

    /// <summary>Mw category accent / neon brush.</summary>
    public IBrush MwNeonBrush { get; }

    /// <summary>Mw outer glow brush (drop-shadow when running).</summary>
    public IBrush MwGlowBrush { get; }

    /// <summary>Mw category glow color for bindable drop-shadow effects.</summary>
    public Color CategoryGlowColor { get; }

    /// <summary>Mw running heat-pulse brush, tinted by category glow.</summary>
    public IBrush MwHeatPulseBrush { get; }

    /// <summary>Mw outer aura brush — bigger, softer radial cloud tinted by category glow.</summary>
    public IBrush MwAuraBrush { get; }

    /// <summary>Forge-themed icon geometry (pit/mold/die/chisel/…).</summary>
    public Geometry? MwIconGeometry { get; }

    /// <summary>Whether the station should render in "running" state.</summary>
    [ObservableProperty]
    private bool _isRunning;

    public PipelineNodeViewModel(NodeDefinition definition, NodeRegistry registry)
    {
        Id = definition.Id;
        TypeKey = definition.TypeKey;
        Title = registry.GetDisplayName(definition.TypeKey);
        Category = registry.GetCategoryForTypeKey(definition.TypeKey);
        Config = definition.Config;
        ConfigPreview = BuildConfigPreview(definition.Config);
        _location = new Point(definition.Position.X, definition.Position.Y);
        _meta = MwOpsMap.Get(definition.TypeKey);

        Color categoryColor = GetCategoryColor(Category);
        Color surfaceColor = ThemeHelper.GetColor("ForgeSurfaceColor", "#1c1820");
        MwOpsMap.CategoryKeys keys = MwOpsMap.KeysFor(_meta.Category);
        Color glowColor = ThemeHelper.GetColor(keys.GlowColor, "#ffd080");

        CategoryBrush = GetCategoryBrush(Category);
        HeaderBrush = BuildHeaderBrush(Category, categoryColor);
        NodeBorderBrush = new SolidColorBrush(Color.FromArgb(0x33, categoryColor.R, categoryColor.G, categoryColor.B));
        NodeBackground = BuildNodeBackground(Category, categoryColor, surfaceColor);
        MwBaseBrush = ThemeHelper.GetBrush(keys.Base, "#3a1808");
        MwNeonBrush = ThemeHelper.GetBrush(keys.Neon, "#ff7a1a");
        MwGlowBrush = ThemeHelper.GetBrush(keys.Glow, "#ffd080");
        CategoryGlowColor = glowColor;
        MwHeatPulseBrush = BuildHeatPulseBrush(glowColor);
        MwAuraBrush = BuildAuraBrush(glowColor);
        MwIconGeometry = ThemeHelper.GetGeometry(MwOpsMap.IconKey(_meta.Icon));

        if (Category != NodeCategory.Source)
        {
            Input.Add(new PipelineConnectorViewModel("In", isInput: true, this));
        }

        if (Category != NodeCategory.Output)
        {
            Output.Add(new PipelineConnectorViewModel("Out", isInput: false, this));
        }
    }

    private static Color GetCategoryColor(NodeCategory category) => category switch
    {
        NodeCategory.Source => ThemeHelper.GetColor("ForgeSourceColor", "#5bb8f5"),
        NodeCategory.Output => ThemeHelper.GetColor("ForgeOutputColor", "#e8932f"),
        _ => ThemeHelper.GetColor("ForgeTransformColor", "#5ce0a0"),
    };

    private static IBrush GetCategoryBrush(NodeCategory category) => category switch
    {
        NodeCategory.Source => ThemeHelper.GetBrush("ForgeSource", "#5bb8f5"),
        NodeCategory.Output => ThemeHelper.GetBrush("ForgeOutput", "#e8932f"),
        _ => ThemeHelper.GetBrush("ForgeTransform", "#5ce0a0"),
    };

    private static IBrush BuildHeaderBrush(NodeCategory category, Color categoryColor)
    {
        byte alpha = (byte)(category == NodeCategory.Output ? 0x0E : 0x0D);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, categoryColor.R, categoryColor.G, categoryColor.B), 0),
                new GradientStop(Colors.Transparent, 1),
            },
        };
    }

    private static IBrush BuildNodeBackground(NodeCategory category, Color categoryColor, Color surfaceColor)
    {
        byte alpha = (byte)(category == NodeCategory.Output ? 0x14 : 0x0F);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, categoryColor.R, categoryColor.G, categoryColor.B), 0),
                new GradientStop(surfaceColor, 0.5),
            },
        };
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

    /// <summary>
    /// Running-aura radial cloud painted behind the station. Maintained alpha
    /// out to 0.65 relative radius so the visible cloud extends past the iron
    /// body covered by the station, then fades to transparent at the border.
    /// </summary>
    private static IBrush BuildAuraBrush(Color glowColor)
    {
        return new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0xD9, glowColor.R, glowColor.G, glowColor.B), 0),
                new GradientStop(Color.FromArgb(0x99, glowColor.R, glowColor.G, glowColor.B), 0.35),
                new GradientStop(Color.FromArgb(0x40, glowColor.R, glowColor.G, glowColor.B), 0.65),
                new GradientStop(Colors.Transparent, 1.0),
            },
        };
    }

    private static string BuildConfigPreview(IDictionary<string, JsonElement> config)
    {
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
