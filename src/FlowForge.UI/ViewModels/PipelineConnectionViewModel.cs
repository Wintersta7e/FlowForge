using CommunityToolkit.Mvvm.ComponentModel;

namespace FlowForge.UI.ViewModels;

public partial class PipelineConnectionViewModel : ViewModelBase
{
    public PipelineConnectorViewModel Source { get; }
    public PipelineConnectorViewModel Target { get; }

    /// <summary>
    /// When true, the pipe renders its mercury/molten liquid layer.
    /// Driven by the pipeline runner's running state.
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    public PipelineConnectionViewModel(PipelineConnectorViewModel source, PipelineConnectorViewModel target)
    {
        Source = source;
        Target = target;
    }
}
