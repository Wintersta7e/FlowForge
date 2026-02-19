using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace FlowForge.Core.Nodes.Transforms;

public class ImageResizeNode : ITransformNode
{
    public string TypeKey => "ImageResize";

    private int? _width;
    private int? _height;
    private string _mode = "max";
    private bool _maintainAspect = true;
    private int? _dpi;

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (config.TryGetValue("width", out JsonElement widthEl) && widthEl.ValueKind == JsonValueKind.Number)
        {
            _width = widthEl.GetInt32();
        }

        if (config.TryGetValue("height", out JsonElement heightEl) && heightEl.ValueKind == JsonValueKind.Number)
        {
            _height = heightEl.GetInt32();
        }

        if (_width is null && _height is null)
        {
            throw new NodeConfigurationException("ImageResize: At least one of 'width' or 'height' is required.");
        }

        if (config.TryGetValue("mode", out JsonElement modeEl) && modeEl.ValueKind == JsonValueKind.String)
        {
            _mode = modeEl.GetString() ?? "max";
        }

        if (config.TryGetValue("maintainAspect", out JsonElement aspectEl))
        {
            _maintainAspect = aspectEl.GetBoolean();
        }

        if (config.TryGetValue("dpi", out JsonElement dpiEl) && dpiEl.ValueKind == JsonValueKind.Number)
        {
            _dpi = dpiEl.GetInt32();
        }
    }

    public async Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        int targetWidth = _width ?? 0;
        int targetHeight = _height ?? 0;

        if (dryRun)
        {
            job.NodeLog.Add($"ImageResize: would resize to {targetWidth}x{targetHeight} ({_mode})");
            return new[] { job };
        }

        using Image image = await Image.LoadAsync(job.CurrentPath, ct);

        ResizeMode resizeMode = _mode.ToLowerInvariant() switch
        {
            "max" => ResizeMode.Max,
            "min" => ResizeMode.Min,
            "crop" => ResizeMode.Crop,
            "pad" => ResizeMode.Pad,
            "stretch" => ResizeMode.Stretch,
            _ => ResizeMode.Max
        };

        if (!_maintainAspect)
        {
            resizeMode = ResizeMode.Stretch;
        }

        // If only one dimension specified, set the other to 0 so ImageSharp auto-calculates
        var resizeOptions = new ResizeOptions
        {
            Size = new Size(targetWidth, targetHeight),
            Mode = resizeMode
        };

        image.Mutate(x => x.Resize(resizeOptions));

        if (_dpi.HasValue)
        {
            image.Metadata.HorizontalResolution = _dpi.Value;
            image.Metadata.VerticalResolution = _dpi.Value;
        }

        await image.SaveAsync(job.CurrentPath, ct);

        job.NodeLog.Add($"ImageResize: resized to {image.Width}x{image.Height} ({_mode})");
        return new[] { job };
    }
}
