using System.Globalization;
using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Nodes.Transforms;

public class MetadataExtractNode : ITransformNode
{
    private readonly ILogger<MetadataExtractNode> _logger;

    public MetadataExtractNode(ILogger<MetadataExtractNode> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public string TypeKey => "MetadataExtract";

    public static IReadOnlyList<ConfigField> ConfigSchema { get; } = new[]
    {
        new ConfigField("keys", ConfigFieldType.MultiLine, Label: "Metadata Keys", Required: true,
            Placeholder: "EXIF:DateTaken, File:SizeBytes",
            Description: "Comma-separated keys. File: SizeBytes, CreatedAt, ModifiedAt. EXIF: DateTaken, CameraModel, CameraMake, GPS, FocalLength, ISO"),
    };

    private List<string> _keys = new();

    public void Configure(IDictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("keys", out JsonElement keysEl) ||
            keysEl.ValueKind == JsonValueKind.Null)
        {
            throw new NodeConfigurationException("MetadataExtract: 'keys' is required.");
        }

        if (keysEl.ValueKind == JsonValueKind.Array)
        {
            _keys = keysEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }
        else if (keysEl.ValueKind == JsonValueKind.String)
        {
            string raw = keysEl.GetString() ?? string.Empty;
            _keys = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        else
        {
            throw new NodeConfigurationException("MetadataExtract: 'keys' must be a JSON array or comma-separated string.");
        }

        if (_keys.Count == 0)
        {
            throw new NodeConfigurationException("MetadataExtract: 'keys' must contain at least one key.");
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (dryRun)
        {
            job.NodeLog.Add("Dry-run: skipping metadata extraction");
            IEnumerable<FileJob> dryRunResult = new[] { job };
            return Task.FromResult(dryRunResult);
        }

        // Read EXIF metadata at most once per file (PERF-05)
        IReadOnlyList<MetadataExtractor.Directory>? exifDirectories = null;
        bool hasExifKeys = _keys.Any(k => k.StartsWith("EXIF:", StringComparison.OrdinalIgnoreCase));
        if (hasExifKeys)
        {
            exifDirectories = ReadExifDirectories(job.CurrentPath);
        }

        foreach (string key in _keys)
        {
            string? value = ExtractValue(key, job.CurrentPath, exifDirectories);
            if (value != null)
            {
                job.Metadata[key] = value;
            }
            else
            {
                job.NodeLog.Add($"MetadataExtract: WARNING — no value found for key '{key}' in '{Path.GetFileName(job.CurrentPath)}'");
            }
        }

        job.NodeLog.Add($"MetadataExtract: extracted {job.Metadata.Count} keys");
        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }

    private static string? ExtractValue(string key, string filePath, IReadOnlyList<MetadataExtractor.Directory>? exifDirectories)
    {
        if (key.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractFileMetadata(key, filePath);
        }

        if (key.StartsWith("EXIF:", StringComparison.OrdinalIgnoreCase) && exifDirectories != null)
        {
            return ExtractExifFromDirectories(key, exifDirectories);
        }

        return null;
    }

    private IReadOnlyList<MetadataExtractor.Directory>? ReadExifDirectories(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (ImageProcessingException ex)
        {
            _logger.LogWarning("MetadataExtract: failed to read EXIF from '{FileName}': {ErrorMessage}",
                Path.GetFileName(filePath), ex.Message);
            return null;
        }
    }

    private static string? ExtractFileMetadata(string key, string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        string fieldName = key["File:".Length..];

        return fieldName.ToLowerInvariant() switch
        {
            "sizebytes" => fileInfo.Length.ToString(CultureInfo.InvariantCulture),
            "createdat" => fileInfo.CreationTimeUtc.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
            "modifiedat" => fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static string? ExtractExifFromDirectories(string key, IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        string fieldName = key["EXIF:".Length..].ToLowerInvariant();

        return fieldName switch
        {
            "datetaken" => ExtractExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal)
                ?? ExtractExifTag(directories, ExifDirectoryBase.TagDateTime),
            "cameramodel" => ExtractExifTag(directories, ExifDirectoryBase.TagModel),
            "cameramake" => ExtractExifTag(directories, ExifDirectoryBase.TagMake),
            "gps" => ExtractGps(directories),
            "focallength" => ExtractExifTag(directories, ExifDirectoryBase.TagFocalLength),
            "iso" => ExtractExifTag(directories, ExifDirectoryBase.TagIsoEquivalent),
            _ => ExtractAnyTag(directories, key["EXIF:".Length..])
        };
    }

    private static string? ExtractExifTag(IReadOnlyList<MetadataExtractor.Directory> directories, int tagType)
    {
        foreach (MetadataExtractor.Directory directory in directories)
        {
            if (directory is ExifSubIfdDirectory or ExifIfd0Directory)
            {
                string? description = directory.GetDescription(tagType);
                if (!string.IsNullOrEmpty(description))
                {
                    return SanitizeForFilename(description);
                }
            }
        }
        return null;
    }

    private static string? ExtractGps(IReadOnlyList<MetadataExtractor.Directory> directories)
    {
        foreach (MetadataExtractor.Directory directory in directories)
        {
            if (directory is GpsDirectory gps && gps.TryGetGeoLocation(out GeoLocation location))
            {
                return $"{location.Latitude:F6},{location.Longitude:F6}";
            }
        }
        return null;
    }

    private static string? ExtractAnyTag(IReadOnlyList<MetadataExtractor.Directory> directories, string tagName)
    {
        foreach (MetadataExtractor.Directory directory in directories)
        {
            foreach (Tag tag in directory.Tags)
            {
                if (tag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
                {
                    return tag.Description != null ? SanitizeForFilename(tag.Description) : null;
                }
            }
        }
        return null;
    }

    private static readonly HashSet<char> InvalidFileNameCharsSet = new(Path.GetInvalidFileNameChars());

    private static string SanitizeForFilename(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (char c in value)
        {
            sb.Append(InvalidFileNameCharsSet.Contains(c) ? '_' : c);
        }
        return sb.ToString().Trim();
    }
}
