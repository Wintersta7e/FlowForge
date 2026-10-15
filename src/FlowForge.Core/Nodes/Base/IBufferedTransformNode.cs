using FlowForge.Core.Execution;
using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>
/// Marker interface for transform nodes that buffer all inputs before yielding results.
/// The pipeline runner must collect all jobs, call TransformAsync for each (which buffers),
/// then call FlushAsync to retrieve the sorted/processed results.
/// </summary>
public interface IBufferedTransformNode
{
    /// <summary>
    /// Returns exactly the jobs the node buffered (those for which TransformAsync returned nothing), each once,
    /// in the node's order: none left out and none added. A job may end here by being marked Failed or Skipped.
    /// The runner stops the run with an <see cref="InvalidOperationException"/> naming the node otherwise.
    /// </summary>
    Task<IEnumerable<FileJob>> FlushAsync(RunContext context, CancellationToken ct = default);
}
