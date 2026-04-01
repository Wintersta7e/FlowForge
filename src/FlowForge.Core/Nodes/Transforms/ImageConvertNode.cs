using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;

namespace FlowForge.Core.Nodes.Transforms;

public class ImageConvertNode : ITransformNode
{
    private static readonly DecoderOptions SafeDecoderOptions = new()
    {
        MaxFrames = 1
    };

    private const long MaxFileSizeBytes = 500 * 1024 * 1024; // 500 MB

    private readonly ILogger<ImageConvertNode> _logger;

    public ImageConvertNode(ILogger<ImageConvertNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "ImageConvert";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("format", ConfigFieldType.Select, Label: "Target Format", Required: true,
            Options: new[] { "jpg", "jpeg", "png", "webp", "bmp", "tiff" }, Description: "Output image format"),
    };

    private string _format = string.Empty;
    private IImageEncoder _encoder = null!;

    public void Configure(IDictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("format", out JsonElement formatEl) ||
            formatEl.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("ImageConvert: 'format' is required.");
        }

        _format = (formatEl.GetString()
            ?? throw new NodeConfigurationException("ImageConvert: 'format' must be a non-null string."))
            .ToLowerInvariant();

        string[] validFormats = { "jpg", "jpeg", "png", "webp", "bmp", "tiff" };
        if (!validFormats.Contains(_format))
        {
            throw new NodeConfigurationException($"ImageConvert: Unsupported format '{_format}'. Supported: {string.Join(", ", validFormats)}");
        }

        _encoder = CreateEncoder(_format);

        _logger.LogDebug("ImageConvert: configured with TargetFormat={TargetFormat}", _format);
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string newPath = BuildTargetPath(job);

        if (dryRun)
        {
            job.CurrentPath = newPath;
            job.NodeLog.Add($"ImageConvert: would convert to {_format}");
            return new[] { job };
        }

        var fileInfo = new FileInfo(job.CurrentPath);
        if (fileInfo.Length > MaxFileSizeBytes)
        {
            job.Status = FileJobStatus.Failed;
            job.NodeLog.Add($"ImageConvert: File too large ({fileInfo.Length / (1024 * 1024)} MB, max 500 MB).");
            return new[] { job };
        }

        return new[] { await ConvertImageAsync(job, newPath, ct).ConfigureAwait(false) };
    }

    private async Task<FileJob> ConvertImageAsync(FileJob job, string newPath, CancellationToken ct)
    {
        string tmpPath = newPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using Image image = await Image.LoadAsync(SafeDecoderOptions, job.CurrentPath, ct).ConfigureAwait(false);
            await image.SaveAsync(tmpPath, _encoder, ct).ConfigureAwait(false);

            FileInfo outputInfo = new(tmpPath);
            if (!outputInfo.Exists || outputInfo.Length == 0)
            {
                _logger.LogWarning("ImageConvert: output file {OutputPath} is missing or empty after save", tmpPath);
                job.Status = FileJobStatus.Failed;
                job.ErrorMessage = "ImageConvert: output file is missing or empty after save. Original preserved.";
                return job;
            }

            File.Move(tmpPath, newPath, overwrite: true);
            DeleteOriginalIfExtensionChanged(job, newPath);

            string oldName = job.FileName;
            job.CurrentPath = newPath;
            job.NodeLog.Add($"ImageConvert: '{oldName}' → '{job.FileName}'");
            return job;
        }
        finally
        {
            CleanupTempFile(tmpPath);
        }
    }

    private string BuildTargetPath(FileJob job)
    {
        string newExtension = _format switch
        {
            "jpg" or "jpeg" => ".jpg",
            "png" => ".png",
            "webp" => ".webp",
            "bmp" => ".bmp",
            "tiff" => ".tiff",
            _ => $".{_format}"
        };

        string nameWithoutExt = Path.GetFileNameWithoutExtension(job.CurrentPath);
        return Path.Combine(job.DirectoryName, nameWithoutExt + newExtension);
    }

    private void DeleteOriginalIfExtensionChanged(FileJob job, string newPath)
    {
        if (string.Equals(job.CurrentPath, newPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(job.CurrentPath))
        {
            return;
        }

        try
        {
            File.Delete(job.CurrentPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "ImageConvert: failed to delete original file {OriginalPath}", job.CurrentPath);
            job.NodeLog.Add($"ImageConvert: Could not delete original '{Path.GetFileName(job.CurrentPath)}': {ex.Message}");
        }
    }

    private void CleanupTempFile(string tmpPath)
    {
        if (!File.Exists(tmpPath))
        {
            return;
        }

        try
        {
            File.Delete(tmpPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "ImageConvert: failed to clean up temp file {TmpPath}", tmpPath);
        }
    }

    private static IImageEncoder CreateEncoder(string format)
    {
        return format switch
        {
            "jpg" or "jpeg" => new JpegEncoder(),
            "png" => new PngEncoder(),
            "webp" => new WebpEncoder(),
            "bmp" => new BmpEncoder(),
            "tiff" => new TiffEncoder(),
            _ => throw new InvalidOperationException($"No encoder for format '{format}'")
        };
    }
}
