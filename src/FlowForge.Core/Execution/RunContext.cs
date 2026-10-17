using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

/// <summary>The state one pipeline run shares with every node it calls.</summary>
public sealed class RunContext : IDisposable
{
    private const string LockFileName = ".lock";

    private const UnixFileMode PrivateFolderMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly Lock _gate = new();

    /// <summary>Live jobs whose content is each staged file; only paths this run handed out are tracked.</summary>
    private readonly Dictionary<string, int> _references = new(StringComparer.Ordinal);

    /// <summary>Each job <see cref="Branch"/> made and no check has taken yet, with the id of the job it was made from.</summary>
    private readonly Dictionary<Guid, Guid> _branchSources = [];

    private string? _runDirectory;
    private FileStream? _lock;

    public RunContext(bool dryRun, string? stagingRoot)
    {
        if (stagingRoot is not null && !Path.IsPathFullyQualified(stagingRoot))
        {
            throw new ArgumentException($"The staging root must be a fully qualified path: '{stagingRoot}'.", nameof(stagingRoot));
        }

        DryRun = dryRun;
        StagingRoot = stagingRoot;
        RunId = Guid.NewGuid().ToString("N");
    }

    public bool DryRun { get; }

    /// <summary>32 lowercase hex digits, unique to this run.</summary>
    public string RunId { get; }

    /// <summary>Every destination this run has claimed, across all its outputs.</summary>
    public DestinationRegistry Destinations { get; } = new();

    /// <summary>Null when the user has no folder of their own to stage in.</summary>
    internal string? StagingRoot { get; }

    /// <summary>
    /// A fresh path for a staged file, in this run's private folder under the staging root. The first call
    /// creates the folder and locks it until <see cref="Dispose"/>. Throws <see cref="InvalidOperationException"/>
    /// in a dry run and <see cref="IOException"/> when there is no staging root, or the root or its parent is a
    /// link or, outside Windows, writable by other users.
    /// </summary>
    public string NewStagingPath(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (DryRun)
        {
            throw new InvalidOperationException("A dry run writes no staged files.");
        }

        lock (_gate)
        {
            _runDirectory ??= CreateRunDirectory();
            string path = Path.Combine(_runDirectory, Guid.NewGuid().ToString("N") + extension);
            _references[path] = 0;
            return path;
        }
    }

    /// <summary>Releases the lock and deletes this run's staging folder.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _lock?.Dispose();
            _lock = null;
            if (_runDirectory is null)
            {
                return;
            }

            try
            {
                Directory.Delete(_runDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next live run's sweep removes it.
            }

            _runDirectory = null;
        }
    }

    /// <summary>
    /// Deletes the staging folders of runs that ended without cleaning up: direct children of
    /// <paramref name="stagingRoot"/> named like a run id whose lock can be taken. A folder whose lock is
    /// held belongs to a live run. Does nothing when the root or its parent is a link or, outside Windows,
    /// writable by other users; never throws.
    /// </summary>
    internal static void SweepStaleRuns(string stagingRoot, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(stagingRoot) || UnsafeRootReason(stagingRoot) is not null)
            {
                return;
            }

            foreach (string child in Directory.EnumerateDirectories(stagingRoot))
            {
                string name = Path.GetFileName(child);
                if (name.Length != 32 || !name.All(char.IsAsciiHexDigitLower) || PathGuard.IsLink(new DirectoryInfo(child)))
                {
                    continue;
                }

                try
                {
                    using (new FileStream(Path.Combine(child, LockFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    {
                    }

                    Directory.Delete(child, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Staging sweep left {Folder} in place", child);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Staging sweep of {Root} failed", stagingRoot);
        }
    }

    /// <summary>Points the job at a staged file and lets go of the content it pointed at before.</summary>
    internal void SetContent(FileJob job, string stagedPath)
    {
        lock (_gate)
        {
            string previous = job.ContentPath;
            if (_references.TryGetValue(stagedPath, out int count))
            {
                _references[stagedPath] = count + 1;
            }

            job.ContentPath = stagedPath;
            Release(previous);
        }
    }

    /// <summary>Counts one more live job reading the job's content.</summary>
    internal void RetainContent(FileJob job)
    {
        lock (_gate)
        {
            if (_references.TryGetValue(job.ContentPath, out int count))
            {
                _references[job.ContentPath] = count + 1;
            }
        }
    }

    /// <summary>
    /// A copy of <paramref name="job"/> for one more path through the pipeline, counted as a live branch from
    /// now on: it reads the job's content until it is given its own and, in a live run, must end before a moved
    /// original is deleted. Every extra job a transform returns must come from here.
    /// </summary>
    internal FileJob Branch(FileJob job)
    {
        FileJob branch = job.CloneForBranch();
        RetainContent(branch);
        if (!DryRun)
        {
            Destinations.RegisterBranch(branch.OriginalPath);
        }

        lock (_gate)
        {
            _branchSources[branch.Id] = job.Id;
        }

        return branch;
    }

    /// <summary>
    /// True, once, when <see cref="Branch"/> made <paramref name="extra"/> from <paramref name="job"/>.
    /// </summary>
    internal bool TakeBranch(FileJob extra, FileJob job)
    {
        lock (_gate)
        {
            return _branchSources.Remove(extra.Id, out Guid source) && source == job.Id;
        }
    }

    /// <summary>Counts one fewer live job reading the job's content; a staged file nobody reads is deleted.</summary>
    internal void ReleaseContent(FileJob job)
    {
        lock (_gate)
        {
            Release(job.ContentPath);
        }
    }

    /// <summary>
    /// Why no file may be staged under <paramref name="stagingRoot"/>: the root or its parent is a link or,
    /// outside Windows, other users can write in it. Null when both are safe.
    /// </summary>
    private static string? UnsafeRootReason(string stagingRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
        foreach (string? path in new[] { root, Path.GetDirectoryName(root) })
        {
            if (path is null)
            {
                continue;
            }

            if (PathGuard.IsLink(new DirectoryInfo(path)))
            {
                return $"The staging folder '{path}' is a link; refusing to write through it.";
            }

            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != UnixFileMode.None)
            {
                return $"The staging folder '{path}' is writable by other users; refusing to write in it.";
            }
        }

        return null;
    }

    private string CreateRunDirectory()
    {
        if (StagingRoot is null)
        {
            throw new IOException("No per-user data folder was found to keep staged copies in; the shared temp folder is not used instead.");
        }

        // Staged files are copies of user content, and an injected root may sit in a folder other users can
        // write. Outside Windows the folders are created private first, so the check below refuses only a folder
        // someone else made or opened up.
        if (!OperatingSystem.IsWindows())
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StagingRoot));
            if (Path.GetDirectoryName(root) is { } parent)
            {
                Directory.CreateDirectory(parent, PrivateFolderMode);
            }

            Directory.CreateDirectory(root, PrivateFolderMode);
        }

        if (UnsafeRootReason(StagingRoot) is { } reason)
        {
            throw new IOException(reason);
        }

        string runDirectory = Path.Combine(StagingRoot, RunId);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(runDirectory);
        }
        else
        {
            Directory.CreateDirectory(runDirectory, PrivateFolderMode);
        }

        _lock = new FileStream(Path.Combine(runDirectory, LockFileName), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        return runDirectory;
    }

    private void Release(string path)
    {
        if (!_references.TryGetValue(path, out int count))
        {
            return;
        }

        if (count > 1)
        {
            _references[path] = count - 1;
            return;
        }

        _references.Remove(path);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Dispose deletes the run folder.
        }
    }
}
