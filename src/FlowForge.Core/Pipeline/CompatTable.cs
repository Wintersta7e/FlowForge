using System.Text.Json;

namespace FlowForge.Core.Pipeline;

/// <summary>
/// Every behaviour change between the versions in <see cref="KnownVersions"/>, in the order notices are listed.
/// An entry lands together with the change it describes.
/// </summary>
internal static class CompatTable
{
    private const string TiffLzwMessage =
        "TIFF files this node writes are now LZW-compressed (lossless) instead of keeping the source file's compression.";

    internal static IReadOnlyList<CompatEntry> Entries { get; } =
    [
        new("tiff-output-lzw", "1.0", "ImageConvert", null, config => HasValue(config, "format", "tiff"), TiffLzwMessage),
        new("tiff-output-lzw", "1.0", "ImageResize", null, _ => true, TiffLzwMessage),
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

    private static bool HasValue(IDictionary<string, JsonElement> config, string key, string value) =>
        config.TryGetValue(key, out JsonElement element)
        && element.ValueKind == JsonValueKind.String
        && string.Equals(element.GetString(), value, StringComparison.OrdinalIgnoreCase);
}
