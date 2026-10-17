using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class DisconnectCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineConnectionViewModel> _connections;
    private readonly IReadOnlyList<PipelineConnectionViewModel> _removed;

    public string Description => "Disconnect nodes";

    public DisconnectCommand(
        ObservableCollection<PipelineConnectionViewModel> connections,
        PipelineConnectionViewModel connection)
        : this(connections, [connection])
    {
    }

    // Every pipe on a port goes in one step, so one Undo puts them all back.
    public DisconnectCommand(
        ObservableCollection<PipelineConnectionViewModel> connections,
        IReadOnlyList<PipelineConnectionViewModel> removed)
    {
        _connections = connections;
        _removed = removed;
    }

    public void Execute()
    {
        foreach (PipelineConnectionViewModel connection in _removed)
        {
            _connections.Remove(connection);
        }

        foreach (PipelineConnectionViewModel connection in _removed)
        {
            connection.Source.IsConnected = HasPipe(connection.Source);
            connection.Target.IsConnected = HasPipe(connection.Target);
        }
    }

    public void Undo()
    {
        foreach (PipelineConnectionViewModel connection in _removed)
        {
            connection.Source.IsConnected = true;
            connection.Target.IsConnected = true;
            _connections.Add(connection);
        }
    }

    private bool HasPipe(PipelineConnectorViewModel connector) =>
        _connections.Any(c => c.Source == connector || c.Target == connector);
}
