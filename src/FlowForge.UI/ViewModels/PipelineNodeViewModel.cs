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

    private static readonly IBrush SourceBrush = GetBrush("MidnightSource", "#58A6FF");
    private static readonly IBrush TransformBrush = GetBrush("MidnightTransform", "#3FB950");
    private static readonly IBrush OutputBrush = GetBrush("MidnightOutput", "#D29922");

    [ObservableProperty]
    private Point _location;

    [ObservableProperty]
    private bool _isSelected;

    public Guid Id { get; }
    public string TypeKey { get; }
    public string Title { get; }
    public NodeCategory Category { get; }
    public ObservableCollection<PipelineConnectorViewModel> Input { get; } = new();
    public ObservableCollection<PipelineConnectorViewModel> Output { get; } = new();
    public Dictionary<string, JsonElement> Config { get; }
    public IBrush HeaderBrush { get; }

    public PipelineNodeViewModel(NodeDefinition definition, NodeRegistry registry)
    {
        Id = definition.Id;
        TypeKey = definition.TypeKey;
        Title = registry.GetDisplayName(definition.TypeKey);
        Category = registry.GetCategoryForTypeKey(definition.TypeKey);
        Config = definition.Config;
        _location = new Point(definition.Position.X, definition.Position.Y);

        HeaderBrush = Category switch
        {
            NodeCategory.Source => SourceBrush,
            NodeCategory.Transform => TransformBrush,
            NodeCategory.Output => OutputBrush,
            _ => TransformBrush
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
}
