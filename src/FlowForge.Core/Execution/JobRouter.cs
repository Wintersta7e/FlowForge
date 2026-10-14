using FlowForge.Core.Models;
using FlowForge.Core.Pipeline;

namespace FlowForge.Core.Execution;

/// <summary>
/// Carries jobs along graph connections. A node's input is what its predecessors emitted along each
/// incoming connection, in connection order. An emitted job goes to the first outgoing connection
/// itself and to every other one as a clone in the same lineage.
/// </summary>
internal sealed class JobRouter
{
    private readonly LineageCounter _lineages;
    private readonly Dictionary<Guid, List<Connection>> _outgoing = new();
    private readonly Dictionary<Guid, List<Connection>> _incoming = new();
    private readonly Dictionary<Connection, List<FileJob>> _carried = new();

    public JobRouter(IReadOnlyList<Connection> connections, LineageCounter lineages)
    {
        _lineages = lineages;
        foreach (Connection connection in connections)
        {
            Append(_outgoing, connection.FromNode, connection);
            Append(_incoming, connection.ToNode, connection);
            _carried[connection] = new List<FileJob>();
        }
    }

    public void Emit(FileJob job, Guid fromNode)
    {
        List<Connection> outgoing = _outgoing[fromNode];
        _carried[outgoing[0]].Add(job);
        foreach (Connection connection in outgoing.Skip(1))
        {
            FileJob clone = job.CloneForBranch();
            _lineages.Branch(job, clone);
            _carried[connection].Add(clone);
        }
    }

    public IReadOnlyList<FileJob> InputOf(Guid node) =>
        _incoming.TryGetValue(node, out List<Connection>? incoming)
            ? incoming.SelectMany(connection => _carried[connection]).ToList()
            : Array.Empty<FileJob>();

    public IReadOnlyList<FileJob> CarriedOn(Connection connection) => _carried[connection];

    private static void Append(Dictionary<Guid, List<Connection>> lists, Guid node, Connection connection)
    {
        if (!lists.TryGetValue(node, out List<Connection>? list))
        {
            list = new List<Connection>();
            lists[node] = list;
        }

        list.Add(connection);
    }
}
