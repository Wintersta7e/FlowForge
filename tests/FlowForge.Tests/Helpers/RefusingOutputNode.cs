using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Helpers;

/// <summary>An output node that refuses every destination and counts the deliveries it is still asked for.</summary>
public sealed class RefusingOutputNode : IOutputNode
{
    public const string RegisteredTypeKey = "RefusingOutput";
    public const string RefusalMessage = "RefusingOutput: destination refused";

    private int _consumeCalls;

    public string TypeKey => RegisteredTypeKey;

    public int ConsumeCalls => Volatile.Read(ref _consumeCalls);

    public void Configure(IDictionary<string, JsonElement> config)
    {
    }

    public DestinationClaim? ClaimDestination(FileJob job, RunContext context) => throw new IOException(RefusalMessage);

    public Task ConsumeAsync(FileJob job, DestinationClaim? claim, RunContext context, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _consumeCalls);
        return Task.CompletedTask;
    }
}
