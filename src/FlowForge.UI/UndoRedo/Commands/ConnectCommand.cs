using System.Collections.ObjectModel;
using FlowForge.UI.ViewModels;

namespace FlowForge.UI.UndoRedo.Commands;

public sealed class ConnectCommand : IUndoableCommand
{
    private readonly ObservableCollection<PipelineConnectionViewModel> _connections;
    private readonly PipelineConnectionViewModel _connection;

    public string Description => "Connect nodes";

    public ConnectCommand(
        ObservableCollection<PipelineConnectionViewModel> connections,
        PipelineConnectionViewModel connection)
    {
        _connections = connections;
        _connection = connection;
    }

    public void Execute()
    {
        _connection.Source.IsConnected = true;
        _connection.Target.IsConnected = true;
        _connections.Add(_connection);
    }

    public void Undo()
    {
        _connection.Source.IsConnected = false;
        _connection.Target.IsConnected = false;
        _connections.Remove(_connection);
    }
}
