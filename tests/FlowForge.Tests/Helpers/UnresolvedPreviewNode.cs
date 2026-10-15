using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// A transform that, in a dry run, marks the job for one file name as an unresolved preview, as a node
/// that needs the file's content would. It changes nothing else.
/// </summary>
internal sealed class UnresolvedPreviewNode : ITransformNode
{
    public const string RegisteredTypeKey = "UnresolvedPreview";

    private readonly string _fileName;

    public UnresolvedPreviewNode(string fileName) => _fileName = fileName;

    public string TypeKey => RegisteredTypeKey;

    public void Configure(IDictionary<string, JsonElement> config)
    {
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
    {
        if (context.DryRun && string.Equals(Path.GetFileName(job.OriginalPath), _fileName, StringComparison.Ordinal))
        {
            job.PreviewMarker = PreviewMarker.Unresolved;
        }

        return Task.FromResult<IEnumerable<FileJob>>([job]);
    }
}
