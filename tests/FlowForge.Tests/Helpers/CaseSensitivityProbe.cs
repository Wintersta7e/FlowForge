namespace FlowForge.Tests.Helpers;

/// <summary>
/// Tells whether a directory's file system tells names apart by letter case.
/// </summary>
internal static class CaseSensitivityProbe
{
    public static bool IsCaseSensitive(string directory)
    {
        string name = "casing-probe-" + Guid.NewGuid().ToString("N") + ".tmp";
        string probe = Path.Combine(directory, name);
        File.WriteAllText(probe, string.Empty);
        try
        {
            return !File.Exists(Path.Combine(directory, name.ToUpperInvariant()));
        }
        finally
        {
            File.Delete(probe);
        }
    }
}
