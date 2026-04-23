namespace FlowForge.Core.Nodes.Base;

internal static class PathGuard
{
    /// <summary>
    /// Throws if <paramref name="candidatePath"/> resolves outside <paramref name="allowedRoot"/>.
    /// </summary>
    public static void EnsureWithinDirectory(string candidatePath, string allowedRoot)
    {
        // Normalize: strip any trailing separator. Path.GetFullPath preserves a
        // user-typed trailing slash, so without trimming, resolvedRoot +
        // DirectorySeparatorChar ended up doubled ("C:\\foo\\\\") and every
        // valid child path was rejected as "resolves outside".
        string resolvedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        string resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));

        if (!resolvedCandidate.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !resolvedCandidate.Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{candidatePath}' resolves outside '{allowedRoot}'.");
        }
    }
}
