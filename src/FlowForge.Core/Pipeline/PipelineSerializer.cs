using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowForge.Core.Pipeline;

public static class PipelineSerializer
{
    private const string VersionKey = "version";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task SaveAsync(PipelineGraph graph, string filePath, CancellationToken ct = default)
    {
        if (!filePath.EndsWith(".ffpipe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Pipeline file must have .ffpipe extension.", nameof(filePath));
        }

        graph.UpdatedAt = DateTime.UtcNow;

        // Every save writes the current version, whatever version the graph was loaded from.
        JsonObject file = JsonSerializer.SerializeToNode(graph, Options)!.AsObject();
        file[VersionKey] = KnownVersions.Current;

        // Atomic write: stream to temp file, then rename
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            FileStream stream = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, file, Options, ct).ConfigureAwait(false);
            }

            File.Move(tmpPath, filePath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
            catch { /* best-effort */ }
        }
    }

    public static async Task<PipelineGraph> LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (!filePath.EndsWith(".ffpipe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Pipeline file must have .ffpipe extension.", nameof(filePath));
        }

        string json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);

        try
        {
            JsonNode root = JsonNode.Parse(json) ?? throw new PipelineLoadException("Deserialized pipeline was null.");
            if (root is JsonObject file)
            {
                CheckVersion(file);
            }

            PipelineGraph graph = root.Deserialize<PipelineGraph>(Options)
                ?? throw new PipelineLoadException("Deserialized pipeline was null.");
            graph.LoadNotices = CompatTable.NoticesFor(graph, graph.Version);
            return graph;
        }
        catch (JsonException ex)
        {
            throw new PipelineLoadException($"Invalid pipeline JSON in '{filePath}': {ex.Message}", ex);
        }
    }

    // A file without a version predates versioning, so it reads as the first version. The key is
    // matched case-sensitively, as the serializer options match every other property.
    private static void CheckVersion(JsonObject file)
    {
        if (!file.TryGetPropertyValue(VersionKey, out JsonNode? version))
        {
            file[VersionKey] = KnownVersions.All[0];
            return;
        }

        string? text = version is JsonValue value && value.TryGetValue(out string? s) ? s : null;
        if (text is null || !KnownVersions.All.Contains(text, StringComparer.Ordinal))
        {
            string found = text ?? version?.ToJsonString() ?? "null";
            throw new PipelineLoadException(
                $"This pipeline was saved by a newer or unknown version of FlowForge (file version {found}). Update FlowForge to open it.");
        }
    }
}
