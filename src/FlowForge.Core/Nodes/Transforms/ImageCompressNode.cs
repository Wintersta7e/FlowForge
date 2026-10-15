using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Imaging;
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
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase) { "jpg", "jpeg", "png", "webp" };

    private static readonly DecoderOptions SafeDecoderOptions = new()
    {
        MaxFrames = 1
    };

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

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, RunContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string targetFormat = _format ?? job.Extension.TrimStart('.').ToLowerInvariant();
        if (PreDecodeChecks.FailIfTargetUnresolved(
                job,
                TypeKey,
                SupportedFormats.Contains(targetFormat) ? targetFormat : null,
                $"unsupported format '{targetFormat}'. Supported: jpg, jpeg, png, webp.",
                _logger)
            || PreDecodeChecks.FailIfTooLarge(job, TypeKey, _logger))
        {
            return new[] { job };
        }

        if (context.DryRun)
        {
            job.PreviewMarker ??= PreviewMarker.CheckedAtRun;
            NameByFormat(job);
            job.NodeLog.Add($"ImageCompress: would compress to quality={_quality}");
            return new[] { job };
        }

        if (await BigTiffGuard.RejectIfBigTiffAsync(job, TypeKey, _logger, ct).ConfigureAwait(false))
        {
            return new[] { job };
        }

        IImageEncoder encoder = GetEncoder(targetFormat);
        string staged = context.NewStagingPath(ImageFormatNames.CanonicalExtension(targetFormat));
        bool referenced = false;
        try
        {
            using Image image = await Image.LoadAsync(SafeDecoderOptions, job.ContentPath, ct).ConfigureAwait(false);
            await image.SaveAsync(staged, encoder, ct).ConfigureAwait(false);

            var stagedInfo = new FileInfo(staged);
            if (!stagedInfo.Exists || stagedInfo.Length == 0)
            {
                _logger.LogWarning("ImageCompress: staged file {StagedPath} is missing or empty after save", staged);
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = "ImageCompress: compressed output is missing or empty. Original preserved.";
                return new[] { job };
            }

            context.SetContent(job, staged);
            referenced = true;
            NameByFormat(job);

            job.NodeLog.Add($"ImageCompress: compressed to quality={_quality} ({stagedInfo.Length} bytes)");
            return new[] { job };
        }
        finally
        {
            if (!referenced)
            {
                try
                {
                    File.Delete(staged);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "ImageCompress: failed to clean up staged file {StagedPath}", staged);
                }
            }
        }
    }

    /// <summary>
    /// With a format override, gives the file the extension of the format it is encoded in, so a name never
    /// promises one format while holding another's bytes.
    /// </summary>
    private void NameByFormat(FileJob job)
    {
        if (!string.IsNullOrEmpty(_format))
        {
            job.CurrentPath = Path.Combine(
                job.DirectoryName,
                Path.GetFileNameWithoutExtension(job.CurrentPath) + ImageFormatNames.CanonicalExtension(_format));
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
