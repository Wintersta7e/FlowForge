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
        string json = JsonSerializer.Serialize(graph, Options);

        // Atomic write: write to temp file, then rename
        string tmpPath = filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, filePath, overwrite: true);
    }

    public static async Task<PipelineGraph> LoadAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Pipeline file not found: '{filePath}'", filePath);
        }

        if (!filePath.EndsWith(".ffpipe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Pipeline file must have .ffpipe extension.", nameof(filePath));
        }

        string json = await File.ReadAllTextAsync(filePath, ct);

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
