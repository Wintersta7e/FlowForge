using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.UI.ViewModels;

public partial class PropertiesViewModel : ViewModelBase
{
    [ObservableProperty]
    private string? _selectedNodeTitle;

    [ObservableProperty]
    private bool _hasSelection;

    public ObservableCollection<ConfigFieldViewModel> Fields { get; } = new();

    public void LoadNode(PipelineNodeViewModel? node, NodeRegistry registry, Action? onConfigChanged = null)
    {
        Fields.Clear();

        if (node is null)
        {
            SelectedNodeTitle = null;
            HasSelection = false;
            return;
        }

        SelectedNodeTitle = node.Title;
        HasSelection = true;

        IReadOnlyList<ConfigField> schema = registry.GetConfigSchema(node.TypeKey);
        foreach (ConfigField field in schema)
        {
            Fields.Add(new ConfigFieldViewModel(field, node.Config, onConfigChanged));
        }
    }
}
