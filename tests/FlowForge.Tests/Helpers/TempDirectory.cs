namespace FlowForge.Tests.Helpers;

public sealed class TempDirectory : IDisposable
{
    public string Path { get; }
    public string OutputPath { get; }

    /// <summary>A staging root for runners in this test; created by the first staged write.</summary>
    public string StagingRoot { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlowForge_Test_" + Guid.NewGuid().ToString("N"));
        OutputPath = System.IO.Path.Combine(Path, "output");
        StagingRoot = System.IO.Path.Combine(Path, ".staging");
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
            DeleteDirectoryLinks(Path);
            Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>
    /// Removes every directory link under <paramref name="directory"/> without descending into one:
    /// a recursive delete throws on a junction on Windows. Deleting a link never touches its target.
    /// </summary>
    private static void DeleteDirectoryLinks(string directory)
    {
        foreach (string child in Directory.GetDirectories(directory))
        {
            if (new DirectoryInfo(child).LinkTarget != null)
            {
                Directory.Delete(child);
            }
            else
            {
                DeleteDirectoryLinks(child);
            }
        }
    }
}
