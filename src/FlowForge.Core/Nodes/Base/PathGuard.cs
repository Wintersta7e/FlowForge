namespace FlowForge.Core.Nodes.Base;

internal static class PathGuard
{
    /// <summary>
    /// Throws if <paramref name="candidatePath"/> resolves outside <paramref name="allowedRoot"/>.
    /// </summary>
    public static void EnsureWithinDirectory(string candidatePath, string allowedRoot)
    {
        string resolvedCandidate = Path.GetFullPath(candidatePath);
        string resolvedRoot = Path.GetFullPath(allowedRoot);

        if (!resolvedCandidate.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !resolvedCandidate.Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{candidatePath}' resolves outside '{allowedRoot}'.");
        }
    }
}
