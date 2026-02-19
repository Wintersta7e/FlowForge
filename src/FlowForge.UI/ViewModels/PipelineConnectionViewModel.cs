namespace FlowForge.UI.ViewModels;

public class PipelineConnectionViewModel : ViewModelBase
{
    public PipelineConnectorViewModel Source { get; }
    public PipelineConnectorViewModel Target { get; }

    public PipelineConnectionViewModel(PipelineConnectorViewModel source, PipelineConnectorViewModel target)
    {
        Source = source;
        Target = target;
    }
}
