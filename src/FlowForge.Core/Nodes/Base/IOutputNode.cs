using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;

namespace FlowForge.Core.Nodes.Base;

/// <summary>Consumes FileJobs and writes final output (copy to folder, in-place, etc.).</summary>
public interface IOutputNode
{
    string TypeKey { get; }
    void Configure(IDictionary<string, JsonElement> config);

    /// <summary>
    /// Decides where the job's file goes before any delivery starts, or returns null when this output writes
    /// no file. The runner calls it one job at a time, in input order; it reads the file system and never
    /// writes. Throws to refuse the destination.
    /// </summary>
    DestinationClaim? ClaimDestination(FileJob job, RunContext context);

    /// <summary>Delivers the job to the destination <see cref="ClaimDestination"/> returned for it.</summary>
    Task ConsumeAsync(FileJob job, DestinationClaim? claim, RunContext context, CancellationToken ct = default);
}
