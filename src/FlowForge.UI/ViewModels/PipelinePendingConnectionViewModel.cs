using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FlowForge.UI.ViewModels;

public partial class PipelinePendingConnectionViewModel : ViewModelBase
{
    private readonly EditorViewModel _editor;

    [ObservableProperty]
    private PipelineConnectorViewModel? _source;

    [ObservableProperty]
    private PipelineConnectorViewModel? _previewTarget;

    [ObservableProperty]
    private bool _isVisible;

    public PipelinePendingConnectionViewModel(EditorViewModel editor)
    {
        _editor = editor;
    }

    [RelayCommand]
    private void Start(PipelineConnectorViewModel connector)
    {
        Source = connector;
        IsVisible = true;
    }

    [RelayCommand]
    private void Finish(PipelineConnectorViewModel? connector)
    {
        if (Source is not null && connector is not null
            && Source != connector
            && Source.IsInput != connector.IsInput
            && Source.Node != connector.Node)
        {
            PipelineConnectorViewModel source = Source.IsInput ? connector : Source;
            PipelineConnectorViewModel target = Source.IsInput ? Source : connector;

            bool alreadyConnected = _editor.Connections.Any(c =>
                c.Source == source && c.Target == target);
            if (!alreadyConnected)
            {
                source.IsConnected = true;
                target.IsConnected = true;
                _editor.Connections.Add(new PipelineConnectionViewModel(source, target));
            }
        }

        Source = null;
        PreviewTarget = null;
        IsVisible = false;
    }
}
