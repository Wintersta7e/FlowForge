using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Helpers;

/// <summary>An output node whose every consume fails with <see cref="ThrownMessage"/>.</summary>
public sealed class ThrowingOutputNode : IOutputNode
{
    public const string RegisteredTypeKey = "ThrowingOutput";
    public const string ThrownMessage = "simulated output failure";

    public string TypeKey => RegisteredTypeKey;

    public void Configure(IDictionary<string, JsonElement> config)
    {
    }

    public DestinationClaim? ClaimDestination(FileJob job, RunContext context) => null;

    public Task ConsumeAsync(FileJob job, DestinationClaim? claim, RunContext context, CancellationToken ct = default) =>
        Task.FromException(new InvalidOperationException(ThrownMessage));
}
