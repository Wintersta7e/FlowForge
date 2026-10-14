namespace FlowForge.Tests.Helpers;

/// <summary>
/// Locates the repository root: the nearest directory at or above a start directory
/// that holds FlowForge.sln.
/// </summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> RootPath = new(() => Find(AppContext.BaseDirectory));

    public static string FullPath => RootPath.Value;

    public static string Combine(params string[] parts) => Path.Combine([FullPath, .. parts]);

    public static string Find(string startDirectory)
    {
        for (DirectoryInfo? dir = new(startDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FlowForge.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No FlowForge.sln found in '{startDirectory}' or any directory above it.");
    }
}
