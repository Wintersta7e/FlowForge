using System.IO.Enumeration;

namespace FlowForge.Core.Nodes.Base;

internal static class PathGuard
{
    private const int MaxLinkReplacements = 40;

    /// <summary>
    /// Throws if <paramref name="candidatePath"/> resolves outside <paramref name="allowedRoot"/>.
    /// </summary>
    public static void EnsureWithinDirectory(string candidatePath, string allowedRoot)
    {
        string resolvedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
        string resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(allowedRoot));

        if (!IsWithinResolvedDirectory(resolvedCandidate, resolvedRoot))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{candidatePath}' resolves outside '{allowedRoot}'.");
        }
    }

    /// <summary>
    /// Predicate form of the containment check operating on already-resolved paths
    /// (avoid re-running <see cref="Path.GetFullPath(string)"/> per file in a loop).
    /// </summary>
    public static bool IsWithinResolvedDirectory(string resolvedCandidate, string resolvedRoot)
    {
        string rootPrefix = NormalizedRootPrefix(resolvedRoot);
        return resolvedCandidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            || resolvedCandidate.Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// True when <paramref name="entry"/> is a symbolic link or a junction. Cloud placeholders and
    /// other reparse points are not links. The attribute is read first, so the link target is read
    /// only for reparse points; a path that does not exist is not a link.
    /// </summary>
    public static bool IsLink(FileSystemInfo entry)
    {
        return (entry.Attributes & FileAttributes.ReparsePoint) != FileAttributes.None && entry.LinkTarget != null;
    }

    /// <summary>
    /// The absolute form of <paramref name="path"/> with every existing linked component replaced by its final
    /// target. Two paths can name one file only when these are equal ignoring case. Throws
    /// <see cref="IOException"/> on a link loop.
    /// </summary>
    public static string ResolvePath(string path) =>
        ResolveLinks(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

    /// <summary>
    /// True when both paths name one file: equal once made absolute and resolved through links, or equal
    /// but for letter case with no differing component present under both spellings. The case check
    /// exists because a case-insensitive folder resolves <c>photo.JPG</c> and <c>photo.jpg</c> to one
    /// file: converting it to <c>.jpg</c> replaces the file itself, and an exact-match delete check would
    /// then delete the result. Hard links are not detected. Exceptions propagate to the caller.
    /// </summary>
    public static bool IsSameFile(string currentPath, string candidatePath)
    {
        string current = ResolvePath(currentPath);
        string candidate = ResolvePath(candidatePath);
        if (string.Equals(current, candidate, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(current) || !File.Exists(candidate))
        {
            return false;
        }

        // Equal but for letter case: one file unless some differing component exists under both spellings.
        string root = Path.GetPathRoot(candidate) ?? string.Empty;
        string[] a = current[root.Length..].Split(Path.DirectorySeparatorChar);
        string[] b = candidate[root.Length..].Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < b.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                string parent = Path.Combine([root, .. b[..i]]);
                if (HasExactEntry(parent, a[i]) && HasExactEntry(parent, b[i]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Replaces the first linked component of <paramref name="fullPath"/> with the link's final target,
    /// and repeats until no existing component is a link.
    /// </summary>
    private static string ResolveLinks(string fullPath)
    {
        string path = fullPath;
        for (int replacements = 0; replacements < MaxLinkReplacements; replacements++)
        {
            string? next = ReplaceFirstLink(path);
            if (next == null)
            {
                return path;
            }

            path = next;
        }

        throw new IOException($"More than {MaxLinkReplacements} links while resolving '{fullPath}'.");
    }

    /// <summary>
    /// Returns <paramref name="path"/> with its first linked component replaced by that link's final
    /// target, or null when the walk reaches a component that does not exist or the end of the path.
    /// </summary>
    private static string? ReplaceFirstLink(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        string[] components = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        string prefix = root;
        for (int i = 0; i < components.Length; i++)
        {
            prefix = Path.Combine(prefix, components[i]);
            var directory = new DirectoryInfo(prefix);
            FileSystemInfo entry = directory.Exists ? directory : new FileInfo(prefix);
            if (!entry.Exists)
            {
                return null;
            }

            if (IsLink(entry))
            {
                FileSystemInfo? target = entry.ResolveLinkTarget(returnFinalTarget: true);
                return target == null ? null : Path.Combine([target.FullName, .. components[(i + 1)..]]);
            }
        }

        return null;
    }

    /// <summary>
    /// True when <paramref name="directory"/> holds an entry named exactly <paramref name="name"/>. No
    /// enumeration pattern is used, so wildcard characters in the name need no escaping.
    /// </summary>
    private static bool HasExactEntry(string directory, string name)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = false,
        };

        return new FileSystemEnumerable<bool>(directory, static (ref FileSystemEntry _) => true, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry e) => e.FileName.Equals(name.AsSpan(), StringComparison.Ordinal),
        }.Any();
    }
}
