using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace FlowForge.Core.Nodes.Transforms;

public class ImageCompressNode : ITransformNode
{
    private static readonly string[] SupportedFormats = { "jpg", "jpeg", "png", "webp" };

    private static readonly DecoderOptions SafeDecoderOptions = new()
    {
        MaxFrames = 1
    };

    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB

    private readonly ILogger<ImageCompressNode> _logger;

    public ImageCompressNode(ILogger<ImageCompressNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "ImageCompress";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("quality", ConfigFieldType.Int, Label: "Quality (1-100)", Required: true, Placeholder: "1-100", Description: "JPEG/WebP quality level (1 = smallest file, 100 = best quality)"),
        new ConfigField("format", ConfigFieldType.Select, Label: "Output Format",
            Options: new[] { "jpg", "jpeg", "png", "webp" }, Description: "Override output format (leave blank to keep original format)"),
    };

    private int _quality = 80;
    private string? _format;

    public void Configure(IDictionary<string, JsonElement> config)
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

        _logger.LogDebug("ImageCompress: configured with Quality={Quality}, Format={Format}", _quality, _format ?? "original");
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (dryRun)
        {
            job.NodeLog.Add($"ImageCompress: would compress to quality={_quality}");
            return new[] { job };
        }

        var fileInfo = new FileInfo(job.CurrentPath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            job.Status = FileJobStatus.Failed;
            job.NodeLog.Add($"ImageCompress: File too large ({fileInfo.Length / (1024 * 1024)} MB, max 500 MB).");
            return new[] { job };
        }

        string tmpPath = job.CurrentPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using Image image = await Image.LoadAsync(SafeDecoderOptions, job.CurrentPath, ct).ConfigureAwait(false);

            string targetFormat = _format ?? job.Extension.TrimStart('.').ToLowerInvariant();
            if (!SupportedFormats.Contains(targetFormat))
            {
                _logger.LogWarning("ImageCompress: unsupported format {Format} for file {FilePath}", targetFormat, job.CurrentPath);
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = $"ImageCompress: unsupported format '{targetFormat}'. Supported: jpg, jpeg, png, webp.";
                return new[] { job };
            }

            IImageEncoder encoder = GetEncoder(targetFormat);

            await image.SaveAsync(tmpPath, encoder, ct).ConfigureAwait(false);

            var tmpInfo = new FileInfo(tmpPath);
            if (!tmpInfo.Exists || tmpInfo.Length == 0)
            {
                _logger.LogWarning("ImageCompress: temp file {TmpPath} is missing or empty after save", tmpPath);
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = "ImageCompress: compressed output is missing or empty. Original preserved.";
                return new[] { job };
            }

            File.Move(tmpPath, job.CurrentPath, overwrite: true);

            long newSize = new FileInfo(job.CurrentPath).Length;
            job.NodeLog.Add($"ImageCompress: compressed to quality={_quality} ({newSize} bytes)");
            return new[] { job };
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "ImageCompress: failed to clean up temp file {TmpPath}", tmpPath);
                }
            }
        }
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
