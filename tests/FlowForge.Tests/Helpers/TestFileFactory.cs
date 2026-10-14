using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FlowForge.Tests.Helpers;

public static class TestFileFactory
{
    /// <summary>Creates a minimal valid JPEG image at the specified path.</summary>
    public static void CreateTestImage(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        image.SaveAsJpeg(filePath);
    }

    /// <summary>Creates a minimal valid PNG image at the specified path.</summary>
    public static void CreateTestPng(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        image.SaveAsPng(filePath);
    }

    /// <summary>Creates a minimal valid BMP image at the specified path.</summary>
    public static void CreateTestBmp(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        image.SaveAsBmp(filePath);
    }

    /// <summary>Creates multiple test images in a directory.</summary>
    public static void CreateTestImages(string directory, params string[] fileNames)
    {
        Directory.CreateDirectory(directory);
        foreach (string fileName in fileNames)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string filePath = Path.Combine(directory, fileName);
            if (string.Equals(ext, ".png", StringComparison.Ordinal))
            {
                CreateTestPng(filePath);
            }
            else
            {
                CreateTestImage(filePath);
            }
        }
    }

    /// <summary>Creates a 24-byte BigTIFF header whose first IFD claims 2^63-1 entries.</summary>
    public static void CreateBigTiffHeader(string filePath, bool bigEndian = false)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null)
        {
            Directory.CreateDirectory(dir);
        }

        byte[] header = bigEndian
            ? new byte[] { 0x4D, 0x4D, 0x00, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x7F, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
            : new byte[] { 0x49, 0x49, 0x2B, 0x00, 0x08, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F };
        File.WriteAllBytes(filePath, header);
    }

    /// <summary>Returns the path of a committed image fixture copied to the test output; throws when it is absent.</summary>
    public static string ImageFixturePath(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "images", fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Image fixture not found: {path}", path);
        }

        return path;
    }
}
