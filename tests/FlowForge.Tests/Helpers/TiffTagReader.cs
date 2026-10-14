using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace FlowForge.Tests.Helpers;

public static class TiffTagReader
{
    /// <summary>Reads the Compression tag (259) of a TIFF's first IFD, independently of the library that wrote it.</summary>
    public static int ReadCompression(string path)
    {
        return ImageMetadataReader.ReadMetadata(path)
            .OfType<ExifIfd0Directory>()
            .First()
            .GetInt32(ExifDirectoryBase.TagCompression);
    }
}
