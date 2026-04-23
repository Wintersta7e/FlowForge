namespace FlowForge.Core.Nodes.Base;

internal static class PathGuard
{
    /// <summary>
    /// Throws if <paramref name="candidatePath"/> resolves outside <paramref name="allowedRoot"/>.
    /// </summary>
    public static void EnsureWithinDirectory(string candidatePath, string allowedRoot)
    {
        // Trim trailing separator before the prefix comparison — otherwise a
        // user-typed "C:\foo\" doubles the separator in resolvedRoot +
        // DirectorySeparatorChar and rejects every valid child.
        string resolvedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        string resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));
        string rootPrefix = NormalizedRootPrefix(resolvedRoot);

        if (!resolvedCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !resolvedCandidate.Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{candidatePath}' resolves outside '{allowedRoot}'.");
        }
    }

    /// <summary>
    /// Returns a root path with exactly one trailing separator, suitable for
    /// use as the prefix in a <c>StartsWith</c> containment check.
    /// <see cref="Path.TrimEndingDirectorySeparator(string)"/> preserves the
    /// separator at the root of a path (so drive roots like <c>C:\</c> and
    /// UNC shares like <c>\\server\share\</c> keep it); appending another
    /// would yield <c>C:\\</c> and reject every child.
    /// </summary>
    public static string NormalizedRootPrefix(string resolvedRoot)
    {
        if (!string.IsNullOrEmpty(resolvedRoot) && resolvedRoot[^1] == Path.DirectorySeparatorChar)
        {
            return resolvedRoot;
        }

        return resolvedRoot + Path.DirectorySeparatorChar;
    }
}
