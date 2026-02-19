using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>
/// Marker interface for transform nodes that buffer all inputs before yielding results.
/// The pipeline runner must collect all jobs, call TransformAsync for each (which buffers),
/// then call FlushAsync to retrieve the sorted/processed results.
/// </summary>
public interface IBufferedTransformNode
{
    Task<IEnumerable<FileJob>> FlushAsync(CancellationToken ct = default);
}
