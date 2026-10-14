using FlowForge.Core.Models;

namespace FlowForge.Core.Execution;

/// <summary>
/// Counts the live branches of each original job: one at discovery, one more per clone or extra
/// emitted job, one fewer per terminal. The terminal that takes the count to zero completes the original.
/// </summary>
internal sealed class LineageCounter
{
    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Guid> _lineageOf = new();
    private readonly Dictionary<Guid, int> _live = new();

    public void AddOriginal(FileJob original)
    {
        lock (_lock)
        {
            _lineageOf[original.Id] = original.Id;
            _live[original.Id] = 1;
        }
    }

    /// <summary>Gives every job a transform emitted for <paramref name="input"/> the input's lineage.</summary>
    public void Join(FileJob input, IReadOnlyList<FileJob> emitted)
    {
        lock (_lock)
        {
            Guid lineage = _lineageOf[input.Id];
            foreach (FileJob job in emitted)
            {
                _lineageOf[job.Id] = lineage;
            }

            _live[lineage] += emitted.Count - 1;
        }
    }

    public void Branch(FileJob job, FileJob clone)
    {
        lock (_lock)
        {
            Guid lineage = _lineageOf[job.Id];
            _lineageOf[clone.Id] = lineage;
            _live[lineage]++;
        }
    }

    /// <summary>Ends one branch of the terminal's lineage; true when it was the last live one.</summary>
    public bool Complete(FileJob terminal)
    {
        lock (_lock)
        {
            Guid lineage = _lineageOf[terminal.Id];
            _live[lineage]--;
            return _live[lineage] == 0;
        }
    }
}
