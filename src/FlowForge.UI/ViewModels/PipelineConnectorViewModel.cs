using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowForge.UI.ViewModels;

public partial class PipelineConnectorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private Point _anchor;

    [ObservableProperty]
    private bool _isConnected;

    private readonly PropertyChangedEventHandler _nodePropertyChangedHandler;

    public bool IsInput { get; }

    public PipelineNodeViewModel Node { get; }

    /// <summary>Category color for this connector (from parent node).</summary>
    public IBrush ConnectorBrush => Node.CategoryBrush;

    public PipelineConnectorViewModel(string title, bool isInput, PipelineNodeViewModel node)
    {
        _title = title;
        IsInput = isInput;
        Node = node;
        _nodePropertyChangedHandler = (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(PipelineNodeViewModel.CategoryBrush), StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(ConnectorBrush));
            }
        };
        node.PropertyChanged += _nodePropertyChangedHandler;
    }

    public void Detach()
    {
        Node.PropertyChanged -= _nodePropertyChangedHandler;
    }
}
