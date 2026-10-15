using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.UI.Services;
using FlowForge.UI.UndoRedo;

namespace FlowForge.UI.ViewModels;

public partial class PropertiesViewModel : ViewModelBase
{
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string? _selectedNodeTitle;

    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Molten Works metadata for the selected node (header row).</summary>
    [ObservableProperty]
    private string? _mwCode;

    [ObservableProperty]
    private string? _mwSub;

    [ObservableProperty]
    private Geometry? _mwIconGeometry;

    [ObservableProperty]
    private IBrush? _mwNeonBrush;

    [ObservableProperty]
    private IBrush? _mwBaseBrush;

    public ObservableCollection<ConfigFieldViewModel> Fields { get; } = new();

    public PropertiesViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
    }

    public void LoadNode(PipelineNodeViewModel? node, NodeRegistry registry, Action<IUndoableCommand>? onConfigChanged = null)
    {
        Fields.Clear();

        if (node is null)
        {
            SelectedNodeTitle = null;
            HasSelection = false;
            MwCode = null;
            MwSub = null;
            MwIconGeometry = null;
            MwNeonBrush = null;
            MwBaseBrush = null;
            return;
        }

        SelectedNodeTitle = node.Title;
        HasSelection = true;
        MwCode = node.MwCode;
        MwSub = node.MwSub;
        MwIconGeometry = node.MwIconGeometry;
        MwNeonBrush = node.MwNeonBrush;
        MwBaseBrush = node.MwBaseBrush;

        IReadOnlyList<ConfigField> schema = registry.GetConfigSchema(node.TypeKey);
        foreach (ConfigField field in schema)
        {
            Fields.Add(new ConfigFieldViewModel(field, node.Config, _dialogService, onConfigChanged));
        }
    }
}
