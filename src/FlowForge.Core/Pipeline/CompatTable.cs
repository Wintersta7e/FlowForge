using System.Text.Json;
using FlowForge.Core.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Core.Pipeline;

/// <summary>
/// Every behaviour change between the versions in <see cref="KnownVersions"/>, in the order notices are listed.
/// An entry lands together with the change it describes.
/// </summary>
internal static class CompatTable
{
    private const string TiffLzwMessage =
        "TIFF files this node writes are now LZW-compressed (lossless) instead of keeping the source file's compression.";

    private const string CollisionSuffixMessage =
        "When two files in one run get the same output name, the later one is now saved with a numbered suffix "
        + "(_1, _2 and so on) instead of failing or replacing the first. An existing file with 'overwrite' off still fails.";

    private const string SourceUnchangedMessage =
        "Transforms no longer change, rename or delete files in the input folder. Their results now appear only in output folders.";

    private const string RenameAtOutputMessage =
        "Renames no longer check or change the input folder. Name clashes are now resolved only in the output folder, "
        + "with a numbered suffix (_1, _2 and so on).";

    private const string MoveAfterDeliveriesMessage =
        "'move' now copies the result and deletes the original only after every delivery of it has succeeded.";

    private const string CompressRenameMessage =
        "A set 'format' now also changes the file's extension to match the encoded bytes "
        + "(photo.png saved as WebP becomes photo.webp) instead of keeping the old name.";

    private const string RoutingMessage =
        "Files now travel only along connections, so each output receives only what is wired to it, instead of every node "
        + "and every output receiving every file. A node that is not on a path from a source to an output, or a connection "
        + "that is not allowed, is now refused when the pipeline runs, naming it.";

    /// <summary>The built-in node types, for entries that apply to every node of a category.</summary>
    private static readonly NodeRegistry BuiltInNodes = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);

    internal static IReadOnlyList<CompatEntry> Entries { get; } =
    [
        new("tiff-output-lzw", "1.0", "ImageConvert", null, config => HasValue(config, "format", "tiff"), TiffLzwMessage),
        new("tiff-output-lzw", "1.0", "ImageResize", null, _ => true, TiffLzwMessage),
        new("output-name-collision-suffix", "1.0", null, graph => HasNode(graph, "FolderOutput"), null, CollisionSuffixMessage),
        new("source-files-unchanged", "1.0", null, HasTransform, null, SourceUnchangedMessage),
        new(
            "rename-collision-at-output",
            "1.0",
            null,
            graph => HasNode(graph, "RenamePattern") || HasNode(graph, "RenameRegex") || HasNode(graph, "RenameAddAffix"),
            null,
            RenameAtOutputMessage),
        new("move-after-all-deliveries", "1.0", "FolderOutput", null, IsMove, MoveAfterDeliveriesMessage),
        new("compress-override-renames", "1.0", "ImageCompress", null, config => HasText(config, "format"), CompressRenameMessage),
        new("graph-runs-along-connections", "1.0", null, RunsDifferentlyAlongConnections, null, RoutingMessage),
    ];

    /// <summary>
    /// The notices for a file saved at <paramref name="fileVersion"/>: every entry from that version up to, not
    /// including, the current one, in table order and then node order.
    /// </summary>
    internal static IReadOnlyList<CompatNotice> NoticesFor(PipelineGraph graph, string fileVersion)
    {
        int file = IndexOf(fileVersion);
        int current = IndexOf(KnownVersions.Current);
        var notices = new List<CompatNotice>();
        foreach (CompatEntry entry in Entries)
        {
            int applies = IndexOf(entry.Version);
            if (applies < file || applies >= current)
            {
                continue;
            }

            if (entry.TypeKey is null)
            {
                if (entry.GraphPredicate?.Invoke(graph) == true)
                {
                    notices.Add(new CompatNotice(entry.Code, null, null, entry.Message));
                }

                continue;
            }

            notices.AddRange(graph.Nodes
                .Where(n => string.Equals(n.TypeKey, entry.TypeKey, StringComparison.Ordinal)
                    && entry.ConfigPredicate?.Invoke(n.Config) == true)
                .Select(n => new CompatNotice(entry.Code, n.Id, n.TypeKey, entry.Message)));
        }

        return notices;
    }

    private static int IndexOf(string version)
    {
        for (int i = 0; i < KnownVersions.All.Count; i++)
        {
            if (string.Equals(KnownVersions.All[i], version, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new ArgumentException($"Unknown pipeline version '{version}'.", nameof(version));
    }

    private static bool HasNode(PipelineGraph graph, string typeKey) =>
        graph.Nodes.Any(n => string.Equals(n.TypeKey, typeKey, StringComparison.Ordinal));

    private static bool HasTransform(PipelineGraph graph) =>
        graph.Nodes.Any(n => BuiltInNodes.IsRegistered(n.TypeKey)
            && BuiltInNodes.GetCategoryForTypeKey(n.TypeKey) == NodeCategory.Transform);

    /// <summary>
    /// True when the run refuses the graph or it is not one chain from a single source to a single output
    /// (repeated connections count once). A single chain runs as it did when every transform ran on every
    /// file and every output received every file.
    /// </summary>
    private static bool RunsDifferentlyAlongConnections(PipelineGraph graph)
    {
        GraphValidationResult validation = PipelineGraphValidator.Validate(graph, BuiltInNodes);
        if (!validation.IsValid)
        {
            return true;
        }

        var incoming = new Dictionary<Guid, int>();
        var outgoing = new Dictionary<Guid, int>();
        foreach (Connection connection in validation.Connections)
        {
            outgoing[connection.FromNode] = outgoing.GetValueOrDefault(connection.FromNode) + 1;
            incoming[connection.ToNode] = incoming.GetValueOrDefault(connection.ToNode) + 1;
        }

        return incoming.Values.Any(count => count > 1)
            || outgoing.Values.Any(count => count > 1)
            || graph.Nodes.Count(n => BuiltInNodes.GetCategoryForTypeKey(n.TypeKey) == NodeCategory.Source) > 1
            || graph.Nodes.Count(n => BuiltInNodes.GetCategoryForTypeKey(n.TypeKey) == NodeCategory.Output) > 1;
    }

    private static bool IsMove(IDictionary<string, JsonElement> config) =>
        config.TryGetValue("mode", out JsonElement element)
        && element.ValueKind == JsonValueKind.String
        && string.Equals(element.GetString(), "move", StringComparison.Ordinal);

    private static bool HasText(IDictionary<string, JsonElement> config, string key) =>
        config.TryGetValue(key, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(element.GetString());

    private static bool HasValue(IDictionary<string, JsonElement> config, string key, string value) =>
        config.TryGetValue(key, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
        && string.Equals(element.GetString(), value, StringComparison.OrdinalIgnoreCase);
}
