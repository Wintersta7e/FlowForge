using FlowForge.Core.Nodes.Base;

namespace FlowForge.Core.Execution;

/// <summary>
/// The destinations one run has claimed, and the one rule that decides where each job's file goes.
/// Claims are made one at a time in the order the run hands jobs to outputs, so a run never replaces its
/// own output and the names it picks do not depend on how many deliveries run at once.
/// </summary>
public sealed class DestinationRegistry
{
    private const int MaxSuffix = 10_000;

    private readonly HashSet<string> _claimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    internal DestinationRegistry()
    {
    }

    /// <summary>
    /// Claims <paramref name="destinationPath"/>, or the first free <c>_n</c> name beside it when this run
    /// already claimed it. Throws <see cref="IOException"/> when the destination exists and
    /// <paramref name="overwrite"/> is off, and <see cref="InvalidOperationException"/> when the path
    /// runs through a link below <paramref name="outputRoot"/>.
    /// </summary>
    public DestinationClaim Claim(string outputRoot, string destinationPath, bool overwrite, string? backupSuffix)
    {
        ArgumentNullException.ThrowIfNull(outputRoot);
        ArgumentNullException.ThrowIfNull(destinationPath);

        lock (_gate)
        {
            // Checked before anything is created: a folder created under a linked folder lands outside the output.
            RefuseLinkedComponents(outputRoot, destinationPath);

            if (IsClaimed(destinationPath))
            {
                string candidate = FirstFreeCandidate(destinationPath);
                Add(candidate);
                return new DestinationClaim(candidate, ClaimKind.New, BackupPath: null);
            }

            if (!IsAbsent(destinationPath))
            {
                if (!overwrite)
                {
                    throw new IOException($"'{destinationPath}' already exists and 'overwrite' is off.");
                }

                if (backupSuffix is null)
                {
                    Add(destinationPath);
                    return new DestinationClaim(destinationPath, ClaimKind.Replace, BackupPath: null);
                }

                string backupPath = destinationPath + backupSuffix;
                if (IsClaimed(backupPath))
                {
                    throw new IOException($"'{destinationPath}' cannot be backed up to '{backupPath}': this run already delivers a file there.");
                }

                Add(destinationPath);
                Add(backupPath);
                return new DestinationClaim(destinationPath, ClaimKind.ReplaceWithBackup, backupPath);
            }

            Add(destinationPath);
            return new DestinationClaim(destinationPath, ClaimKind.New, BackupPath: null);
        }
    }

    /// <summary>A dangling link counts as present.</summary>
    private static bool IsAbsent(string path) =>
        !File.Exists(path) && !Directory.Exists(path) && new FileInfo(path).LinkTarget == null;

    /// <summary>
    /// Refuses the destination when it, or any existing folder between the output folder and it, is a
    /// link. The output folder itself may be a link. Only attributes are read, so a dry run reports the
    /// same refusal.
    /// </summary>
    private static void RefuseLinkedComponents(string outputRoot, string destinationPath)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        string current = root;
        foreach (string component in Path.GetRelativePath(root, Path.GetFullPath(destinationPath)).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, component);
            if (!RefuseLink(current))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Throws when the entry at <paramref name="path"/> is a link; returns whether an entry exists there.
    /// </summary>
    private static bool RefuseLink(string path)
    {
        var directory = new DirectoryInfo(path);
        FileSystemInfo entry = directory.Exists ? directory : new FileInfo(path);
        if (entry.Exists && PathGuard.IsLink(entry))
        {
            throw new InvalidOperationException($"FolderOutput: '{path}' is a link; refusing to deliver through it.");
        }

        return entry.Exists;
    }

    private string FirstFreeCandidate(string destinationPath)
    {
        string directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
        string extension = Path.GetExtension(destinationPath);
        for (int suffix = 1; suffix <= MaxSuffix; suffix++)
        {
            string candidate = Path.Combine(directory, $"{nameWithoutExtension}_{suffix}{extension}");
            if (!IsClaimed(candidate) && IsAbsent(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"'{destinationPath}' is already delivered in this run, and no free name was found after 10,000 tries.");
    }

    private bool IsClaimed(string path) => _claimed.Contains(Path.GetFullPath(path));

    private void Add(string path) => _claimed.Add(Path.GetFullPath(path));
}
