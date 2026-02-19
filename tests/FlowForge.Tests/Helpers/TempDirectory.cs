namespace FlowForge.Tests.Helpers;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }
    public string OutputPath { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlowForge_Test_" + Guid.NewGuid().ToString("N"));
        OutputPath = System.IO.Path.Combine(Path, "output");
        Directory.CreateDirectory(Path);
        Directory.CreateDirectory(OutputPath);
    }

    public void CreateFiles(params string[] fileNames)
    {
        foreach (string fileName in fileNames)
        {
            string filePath = System.IO.Path.Combine(Path, fileName);
            string? dir = System.IO.Path.GetDirectoryName(filePath);
            if (dir != null)
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(filePath, $"test content: {fileName}");
        }
    }

    public string[] OutputFiles =>
        Directory.Exists(OutputPath)
            ? Directory.GetFiles(OutputPath).Select(f => System.IO.Path.GetFileName(f)!).ToArray()
            : Array.Empty<string>();

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
