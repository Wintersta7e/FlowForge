using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
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
    public string TypeKey => "ImageConvert";

    private string _format = string.Empty;

    public void Configure(Dictionary<string, JsonElement> config)
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
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        string newExtension = _format switch
        {
            "jpg" or "jpeg" => ".jpg",
            "png" => ".png",
            "webp" => ".webp",
            "bmp" => ".bmp",
            "tiff" => ".tiff",
            _ => $".{_format}"
        };

        string directory = job.DirectoryName;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(job.CurrentPath);
        string newPath = Path.Combine(directory, nameWithoutExt + newExtension);

        if (dryRun)
        {
            job.CurrentPath = newPath;
            job.NodeLog.Add($"ImageConvert: would convert to {_format}");
            return new[] { job };
        }

        using Image image = await Image.LoadAsync(job.CurrentPath, ct);

        IImageEncoder encoder = GetEncoder();
        await image.SaveAsync(newPath, encoder, ct);

        // Remove old file if extension changed
        if (!string.Equals(job.CurrentPath, newPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(job.CurrentPath))
        {
            File.Delete(job.CurrentPath);
        }

        string oldName = job.FileName;
        job.CurrentPath = newPath;
        job.NodeLog.Add($"ImageConvert: '{oldName}' → '{job.FileName}'");
        return new[] { job };
    }

    private IImageEncoder GetEncoder()
    {
        return _format switch
        {
            "jpg" or "jpeg" => new JpegEncoder(),
            "png" => new PngEncoder(),
            "webp" => new WebpEncoder(),
            "bmp" => new BmpEncoder(),
            "tiff" => new TiffEncoder(),
            _ => throw new InvalidOperationException($"No encoder for format '{_format}'")
        };
    }
}
