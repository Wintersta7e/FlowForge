using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowForge.UI.UndoRedo.Commands;

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
            if (!alreadyConnected && !Reaches(target.Node, source.Node))
            {
                var connection = new PipelineConnectionViewModel(source, target);
                _editor.UndoRedo.Execute(new ConnectCommand(_editor.Connections, connection));
            }
        }

        Source = null;
        PreviewTarget = null;
        IsVisible = false;
    }

    // True when the connections already lead from one station to the other, so a pipe back
    // would close a loop.
    private bool Reaches(PipelineNodeViewModel from, PipelineNodeViewModel to)
    {
        var visited = new HashSet<PipelineNodeViewModel>();
        var pending = new Stack<PipelineNodeViewModel>();
        pending.Push(from);
        while (pending.Count > 0)
        {
            PipelineNodeViewModel node = pending.Pop();
            if (node == to)
            {
                return true;
            }

            if (!visited.Add(node))
            {
                continue;
            }

            foreach (PipelineConnectionViewModel connection in _editor.Connections)
            {
                if (connection.Source.Node == node)
                {
                    pending.Push(connection.Target.Node);
                }
            }
        }

        return false;
    }
}
