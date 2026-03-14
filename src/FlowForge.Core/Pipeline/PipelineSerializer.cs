using System.Text.Json;

namespace FlowForge.Core.Pipeline;

public static class PipelineSerializer
{
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

        // Atomic write: stream to temp file, then rename
        string tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using FileStream stream = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await JsonSerializer.SerializeAsync(stream, graph, Options, ct).ConfigureAwait(false);
            File.Move(tmpPath, filePath, overwrite: true);
        }
        finally
        {
            try
            { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
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
            PipelineGraph? graph = JsonSerializer.Deserialize<PipelineGraph>(json, Options);
            return graph ?? throw new PipelineLoadException("Deserialized pipeline was null.");
        }
        catch (JsonException ex)
        {
            throw new PipelineLoadException($"Invalid pipeline JSON in '{filePath}': {ex.Message}", ex);
        }
    }
}
