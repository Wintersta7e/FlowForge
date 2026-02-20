using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FlowForge.Tests.Helpers;

public static class TestFileFactory
{
    /// <summary>Creates a minimal valid JPEG image at the specified path.</summary>
    public static void CreateTestImage(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        image.SaveAsJpeg(filePath);
    }

    /// <summary>Creates a minimal valid PNG image at the specified path.</summary>
    public static void CreateTestPng(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);

        using var image = new Image<Rgba32>(width, height, Color.CornflowerBlue);
        image.SaveAsPng(filePath);
    }

    /// <summary>Creates a minimal valid BMP image at the specified path.</summary>
    public static void CreateTestBmp(string filePath, int width = 100, int height = 100)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (dir != null) Directory.CreateDirectory(dir);

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
            if (ext == ".png")
                CreateTestPng(filePath);
            else
                CreateTestImage(filePath);
        }
    }
}
