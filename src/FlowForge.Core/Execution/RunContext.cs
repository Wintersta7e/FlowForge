using FlowForge.Core.Models;
using FlowForge.Core.Nodes.Base;
using Microsoft.Extensions.Logging;

namespace FlowForge.Core.Execution;

/// <summary>The state one pipeline run shares with every node it calls.</summary>
public sealed class RunContext : IDisposable
{
    private const string LockFileName = ".lock";

    private readonly Lock _gate = new();

    /// <summary>Live jobs whose content is each staged file; only paths this run handed out are tracked.</summary>
    private readonly Dictionary<string, int> _references = new(StringComparer.Ordinal);

    private string? _runDirectory;
    private FileStream? _lock;

    public RunContext(bool dryRun, string stagingRoot)
    {
        ArgumentNullException.ThrowIfNull(stagingRoot);
        if (!Path.IsPathFullyQualified(stagingRoot))
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

    internal string StagingRoot { get; }

    /// <summary>
    /// A fresh path for a staged file, in this run's private folder under the staging root. The first call
    /// creates the folder and locks it until <see cref="Dispose"/>. Throws <see cref="InvalidOperationException"/>
    /// in a dry run and <see cref="IOException"/> when the staging root or its parent is a link.
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
    /// held belongs to a live run. Does nothing when the root or its parent is a link; never throws.
    /// </summary>
    internal static void SweepStaleRuns(string stagingRoot, ILogger logger)
    {
        try
        {
            if (LinkedRootComponent(stagingRoot) is not null || !Directory.Exists(stagingRoot))
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

    /// <summary>Counts one fewer live job reading the job's content; a staged file nobody reads is deleted.</summary>
    internal void ReleaseContent(FileJob job)
    {
        lock (_gate)
        {
            Release(job.ContentPath);
        }
    }

    private static string? LinkedRootComponent(string stagingRoot)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
        foreach (string? path in new[] { root, Path.GetDirectoryName(root) })
        {
            if (path is not null && PathGuard.IsLink(new DirectoryInfo(path)))
            {
                return path;
            }
        }

        return null;
    }

    private string CreateRunDirectory()
    {
        if (LinkedRootComponent(StagingRoot) is { } link)
        {
            throw new IOException($"The staging folder '{link}' is a link; refusing to write through it.");
        }

        string runDirectory = Path.Combine(StagingRoot, RunId);

        // Staged files are copies of user content under a shared temp folder.
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(runDirectory);
        }
        else
        {
            Directory.CreateDirectory(runDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
