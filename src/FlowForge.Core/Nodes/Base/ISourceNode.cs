using System.Text.Json;
using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>Produces a stream of FileJobs from an external source (folder, picker, etc.).</summary>
public interface ISourceNode
{
    string TypeKey { get; }
    void Configure(Dictionary<string, JsonElement> config);
    IAsyncEnumerable<FileJob> ProduceAsync(CancellationToken ct = default);
}
