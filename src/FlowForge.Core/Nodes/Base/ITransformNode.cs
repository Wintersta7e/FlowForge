using System.Text.Json;
using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>
/// Transforms a single FileJob. Returns the same job (mutated), a new job,
/// multiple jobs (fan-out), or empty to drop the file from the pipeline.
/// </summary>
public interface ITransformNode
{
    string TypeKey { get; }
    void Configure(Dictionary<string, JsonElement> config);
    Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default);
}
