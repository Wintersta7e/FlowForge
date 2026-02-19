using Avalonia;
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

    public bool IsInput { get; }

    public PipelineNodeViewModel Node { get; }

    public PipelineConnectorViewModel(string title, bool isInput, PipelineNodeViewModel node)
    {
        _title = title;
        IsInput = isInput;
        Node = node;
    }
}
