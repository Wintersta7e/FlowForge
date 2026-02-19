using System.Text.Json;
using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace FlowForge.Core.Nodes.Transforms;

public class MetadataExtractNode : ITransformNode
{
    public string TypeKey => "MetadataExtract";

    private List<string> _keys = new();

    public void Configure(Dictionary<string, JsonElement> config)
    {
        if (!config.TryGetValue("keys", out JsonElement keysEl) ||
            keysEl.ValueKind != JsonValueKind.Array)
        {
            throw new NodeConfigurationException("MetadataExtract: 'keys' array is required.");
        }

        _keys = keysEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();

        if (_keys.Count == 0)
        {
            throw new NodeConfigurationException("MetadataExtract: 'keys' must contain at least one key.");
        }
    }

    public Task<IEnumerable<FileJob>> TransformAsync(FileJob job, bool dryRun, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        foreach (string key in _keys)
        {
            string? value = ExtractValue(key, job.CurrentPath);
            if (value != null)
            {
                job.Metadata[key] = value;
            }
        }

        job.NodeLog.Add($"MetadataExtract: extracted {job.Metadata.Count} keys");
        IEnumerable<FileJob> result = new[] { job };
        return Task.FromResult(result);
    }

    private static string? ExtractValue(string key, string filePath)
    {
        // File-level metadata (doesn't need MetadataExtractor)
        if (key.StartsWith("File:", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractFileMetadata(key, filePath);
        }

        // EXIF metadata
        if (key.StartsWith("EXIF:", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractExifMetadata(key, filePath);
        }

        return null;
    }

    private static string? ExtractFileMetadata(string key, string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var fileInfo = new FileInfo(filePath);
        string fieldName = key["File:".Length..];

        return fieldName.ToLowerInvariant() switch
        {
            "sizebytes" => fileInfo.Length.ToString(),
            "createdat" => fileInfo.CreationTimeUtc.ToString("yyyy-MM-dd_HH-mm-ss"),
            "modifiedat" => fileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd_HH-mm-ss"),
            _ => null
        };
    }

    private static string? ExtractExifMetadata(string key, string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            IReadOnlyList<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(filePath);
            string fieldName = key["EXIF:".Length..].ToLowerInvariant();

            return fieldName switch
            {
                "datetaken" => ExtractExifTag(directories, ExifDirectoryBase.TagDateTimeOriginal)
                    ?? ExtractExifTag(directories, ExifDirectoryBase.TagDateTime),
                "cameramodel" => ExtractExifTag(directories, ExifDirectoryBase.TagModel),
                "cameramake" => ExtractExifTag(directories, ExifDirectoryBase.TagMake),
                "gps" => ExtractGps(directories),
                "focalLength" => ExtractExifTag(directories, ExifDirectoryBase.TagFocalLength),
                "iso" => ExtractExifTag(directories, ExifDirectoryBase.TagIsoEquivalent),
                _ => ExtractAnyTag(directories, key["EXIF:".Length..])
            };
        }
        catch (ImageProcessingException)
        {
            return null;
        }
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

    private static string SanitizeForFilename(string value)
    {
        // Replace characters that are invalid in filenames
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = value;
        foreach (char c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }
        return sanitized.Trim();
    }
}
