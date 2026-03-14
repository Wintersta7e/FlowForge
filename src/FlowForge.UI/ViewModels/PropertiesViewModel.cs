using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.UndoRedo;

namespace FlowForge.UI.ViewModels;

public partial class PropertiesViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _selectedNodeTitle;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private IBrush? _badgeForeground;

    [ObservableProperty]
    private IBrush? _badgeBackground;

    public ObservableCollection<ConfigFieldViewModel> Fields { get; } = new();

    public void LoadNode(PipelineNodeViewModel? node, NodeRegistry registry, Action<IUndoableCommand>? onConfigChanged = null)
    {
        Fields.Clear();

        if (node is null)
        {
            SelectedNodeTitle = null;
            HasSelection = false;
            BadgeForeground = null;
            BadgeBackground = null;
            return;
        }

        SelectedNodeTitle = node.Title;
        HasSelection = true;
        BadgeForeground = node.HeaderBrush;
        BadgeBackground = node.Category switch
        {
            NodeCategory.Source => ThemeHelper.GetBrush("ForgeSourceDim", "#265bb8f5"),
            NodeCategory.Transform => ThemeHelper.GetBrush("ForgeTransformDim", "#265ce0a0"),
            NodeCategory.Output => ThemeHelper.GetBrush("ForgeOutputDim", "#26e8932f"),
            _ => ThemeHelper.GetBrush("ForgeElevated", "#252029")
        };

        IReadOnlyList<ConfigField> schema = registry.GetConfigSchema(node.TypeKey);
        foreach (ConfigField field in schema)
        {
            Fields.Add(new ConfigFieldViewModel(field, node.Config, onConfigChanged));
        }
    }
}
