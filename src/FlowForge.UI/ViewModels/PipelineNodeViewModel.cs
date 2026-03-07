using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Pipeline;

namespace FlowForge.UI.ViewModels;

public partial class PipelineNodeViewModel : ViewModelBase
{
    private static IBrush GetBrush(string key, string fallback)
    {
        if (Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? resource) == true && resource is IBrush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Color.Parse(fallback));
    }

    // Category solid colors (for text, connectors)
    private static readonly IBrush SourceBrush = GetBrush("ForgeSource", "#5bb8f5");
    private static readonly IBrush TransformBrush = GetBrush("ForgeTransform", "#5ce0a0");
    private static readonly IBrush OutputBrush = GetBrush("ForgeOutput", "#e8932f");

    // Category border colors (~20% opacity)
    private static readonly IBrush SourceBorderBrush = new SolidColorBrush(Color.Parse("#335bb8f5"));
    private static readonly IBrush TransformBorderBrush = new SolidColorBrush(Color.Parse("#335ce0a0"));
    private static readonly IBrush OutputBorderBrush = new SolidColorBrush(Color.Parse("#33e8932f"));

    // Category header gradients (subtle, not solid blocks)
    private static readonly IBrush SourceHeaderGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0D5bb8f5"), 0), new GradientStop(Colors.Transparent, 1) }
    };

    private static readonly IBrush TransformHeaderGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0D5ce0a0"), 0), new GradientStop(Colors.Transparent, 1) }
    };

    private static readonly IBrush OutputHeaderGradient = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0Ee8932f"), 0), new GradientStop(Colors.Transparent, 1) }
    };

    // Category-tinted gradient backgrounds
    private static readonly IBrush SourceNodeBg = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0F5bb8f5"), 0), new GradientStop(Color.Parse("#1c1820"), 0.5) }
    };

    private static readonly IBrush TransformNodeBg = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#0F5ce0a0"), 0), new GradientStop(Color.Parse("#1c1820"), 0.5) }
    };

    private static readonly IBrush OutputNodeBg = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#14e8932f"), 0), new GradientStop(Color.Parse("#1c1820"), 0.5) }
    };

    // Icon emoji per node type
    private static readonly Dictionary<string, string> NodeIcons = new()
    {
        ["FolderInput"] = "\U0001F4C1",
        ["RenamePattern"] = "\u270E",
        ["RenameRegex"] = ".*",
        ["RenameAddAffix"] = "+a",
        ["Filter"] = "\U0001F50D",
        ["Sort"] = "\u21C5",
        ["ImageResize"] = "\U0001F4F8",
        ["ImageConvert"] = "\U0001F3A8",
        ["ImageCompress"] = "\U0001F4E6",
        ["MetadataExtract"] = "\U0001F4C4",
        ["FolderOutput"] = "\U0001F4E5",
    };

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
    public Dictionary<string, JsonElement> Config { get; }

    /// <summary>Solid category color for text and connector fills.</summary>
    public IBrush CategoryBrush { get; }

    /// <summary>Subtle gradient for the node header area.</summary>
    public IBrush HeaderBrush { get; }

    /// <summary>20% opacity category color for the node border.</summary>
    public IBrush NodeBorderBrush { get; }

    /// <summary>Top-down gradient with subtle category tint.</summary>
    public IBrush NodeBackground { get; }

    public PipelineNodeViewModel(NodeDefinition definition, NodeRegistry registry)
    {
        Id = definition.Id;
        TypeKey = definition.TypeKey;
        Title = registry.GetDisplayName(definition.TypeKey);
        Category = registry.GetCategoryForTypeKey(definition.TypeKey);
        Config = definition.Config;
        IconEmoji = NodeIcons.GetValueOrDefault(definition.TypeKey, "\u2699");
        ConfigPreview = BuildConfigPreview(definition.Config);
        _location = new Point(definition.Position.X, definition.Position.Y);

        CategoryBrush = Category switch
        {
            NodeCategory.Source => SourceBrush,
            NodeCategory.Transform => TransformBrush,
            NodeCategory.Output => OutputBrush,
            _ => TransformBrush
        };

        HeaderBrush = Category switch
        {
            NodeCategory.Source => SourceHeaderGradient,
            NodeCategory.Transform => TransformHeaderGradient,
            NodeCategory.Output => OutputHeaderGradient,
            _ => TransformHeaderGradient
        };

        NodeBorderBrush = Category switch
        {
            NodeCategory.Source => SourceBorderBrush,
            NodeCategory.Transform => TransformBorderBrush,
            NodeCategory.Output => OutputBorderBrush,
            _ => TransformBorderBrush
        };

        NodeBackground = Category switch
        {
            NodeCategory.Source => SourceNodeBg,
            NodeCategory.Transform => TransformNodeBg,
            NodeCategory.Output => OutputNodeBg,
            _ => TransformNodeBg
        };

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

    private static string BuildConfigPreview(Dictionary<string, JsonElement> config)
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
