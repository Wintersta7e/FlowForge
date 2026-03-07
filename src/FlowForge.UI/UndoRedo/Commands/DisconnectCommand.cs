using System.Collections.ObjectModel;
using System.Linq;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class DisconnectCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineConnectionViewModel> _connections;
    private readonly PipelineConnectionViewModel _connection;

    public string Description => "Disconnect nodes";

    public DisconnectCommand(
        ObservableCollection<PipelineConnectionViewModel> connections,
        PipelineConnectionViewModel connection)
    {
        _connections = connections;
        _connection = connection;
    }

    public void Execute()
    {
        _connections.Remove(_connection);
        _connection.Source.IsConnected = _connections.Any(c =>
            c.Source == _connection.Source || c.Target == _connection.Source);
        _connection.Target.IsConnected = _connections.Any(c =>
            c.Source == _connection.Target || c.Target == _connection.Target);
    }

    public void Undo()
    {
        _connection.Source.IsConnected = true;
        _connection.Target.IsConnected = true;
        _connections.Add(_connection);
    }
}
