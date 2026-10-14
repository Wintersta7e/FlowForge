namespace FlowForge.Core.Imaging;

/// <summary>The one map from an output format to the extension a file holding it is named with.</summary>
internal static class ImageFormatNames
{
    /// <summary>The extension, dot included, for a file encoded as <paramref name="format"/> (lowercase).</summary>
    internal static string CanonicalExtension(string format) => format switch
    {
        "jpg" or "jpeg" => ".jpg",
        "png" => ".png",
        "webp" => ".webp",
        "bmp" => ".bmp",
        "tiff" => ".tiff",
        _ => $".{format}",
    };
}
