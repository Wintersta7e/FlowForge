using System.Text.Json;
using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>Consumes FileJobs and writes final output (copy to folder, in-place, etc.).</summary>
public interface IOutputNode
{
    string TypeKey { get; }
    void Configure(Dictionary<string, JsonElement> config);
    Task ConsumeAsync(FileJob job, bool dryRun, CancellationToken ct = default);
}
