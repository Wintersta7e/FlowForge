using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace FlowForge.Core.Nodes.Transforms;

public class ImageCompressNode : ITransformNode
{
    public string TypeKey => "ImageCompress";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("quality", ConfigFieldType.Int, Label: "Quality (1-100)", Required: true, Placeholder: "1-100"),
        new ConfigField("format", ConfigFieldType.Select, Label: "Output Format",
            Options: new[] { "jpg", "jpeg", "png", "webp" }),
    };

    private int _quality = 80;
    private string? _format;

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("quality", out JsonElement qualityEl) ||
            qualityEl.ValueKind != JsonValueKind.Number)
        {
            throw new NodeConfigurationException("ImageCompress: 'quality' (1-100) is required.");
        }

        _quality = qualityEl.GetInt32();
        if (_quality < 1 || _quality > 100)
        {
            throw new NodeConfigurationException("ImageCompress: 'quality' must be between 1 and 100.");
        }

        if (config.TryGetValue("format", out JsonElement formatEl) &&
            formatEl.ValueKind == JsonValueKind.String)
        {
            _format = formatEl.GetString()?.ToLowerInvariant();
        }
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (dryRun)
        {
            job.NodeLog.Add($"ImageCompress: would compress to quality={_quality}");
            return new[] { job };
        }

        using Image image = await Image.LoadAsync(job.CurrentPath, ct);

        string targetFormat = _format ?? job.Extension.TrimStart('.').ToLowerInvariant();
        string[] supportedFormats = { "jpg", "jpeg", "png", "webp" };
        if (!supportedFormats.Contains(targetFormat))
        {
            job.Status = FileJobStatus.Failed;
            job.ErrorMessage = $"ImageCompress: unsupported format '{targetFormat}'. Supported: jpg, jpeg, png, webp.";
            return new[] { job };
        }
        IImageEncoder encoder = GetEncoder(targetFormat);

        await image.SaveAsync(job.CurrentPath, encoder, ct);

        long newSize = new FileInfo(job.CurrentPath).Length;
        job.NodeLog.Add($"ImageCompress: compressed to quality={_quality} ({newSize} bytes)");
        return new[] { job };
    }

    private IImageEncoder GetEncoder(string format)
    {
        return format switch
        {
            "jpg" or "jpeg" => new JpegEncoder { Quality = _quality },
            "png" => new PngEncoder { CompressionLevel = QualityToPngCompression(_quality) },
            "webp" => new WebpEncoder { Quality = _quality },
            _ => throw new InvalidOperationException(
                $"ImageCompress does not support format '{format}'. Supported: jpg, jpeg, png, webp.")
        };
    }

    private static PngCompressionLevel QualityToPngCompression(int quality)
    {
        // Map 1-100 quality to PNG compression levels (inverted: high quality = low compression)
        return quality switch
        {
            >= 90 => PngCompressionLevel.Level1,
            >= 70 => PngCompressionLevel.Level3,
            >= 50 => PngCompressionLevel.Level5,
            >= 30 => PngCompressionLevel.Level7,
            _ => PngCompressionLevel.Level9
        };
    }
}
