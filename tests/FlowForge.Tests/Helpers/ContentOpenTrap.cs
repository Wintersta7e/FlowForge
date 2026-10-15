using System.Diagnostics;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Makes every content open of the armed files fail until disposed, managed or native. Each file is held open
/// with <see cref="FileShare.None"/>; off Windows its mode is also cleared, because a lock held in this process
/// does not stop a native reader there. Listing folders and reading sizes and dates still work. Never take a
/// <see cref="TreeSnapshot"/> while armed: hashing opens files.
/// </summary>
internal sealed class ContentOpenTrap : IDisposable
{
    private readonly List<FileStream> _held = [];
    private readonly List<(string Path, UnixFileMode Mode)> _priorModes = [];

    private ContentOpenTrap()
    {
    }

    /// <summary>Arms every file, then checks that each one refuses a managed open and, off Windows, <c>cat</c>.</summary>
    public static ContentOpenTrap Arm(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("No file to arm.", nameof(files));
        }

        if (!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess)
        {
            throw new InvalidOperationException("ContentOpenTrap: the tests run as a privileged user, who can read a file whatever its mode.");
        }

        var trap = new ContentOpenTrap();
        try
        {
            foreach (string file in files)
            {
                trap._held.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None));
                if (!OperatingSystem.IsWindows())
                {
                    trap._priorModes.Add((file, File.GetUnixFileMode(file)));
                    File.SetUnixFileMode(file, UnixFileMode.None);
                }
            }

            foreach (string file in files)
            {
                CheckArmed(file);
            }
        }
        catch
        {
            trap.Dispose();
            throw;
        }

        return trap;
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach ((string path, UnixFileMode mode) in _priorModes)
            {
                File.SetUnixFileMode(path, mode);
            }
        }

        _priorModes.Clear();
        foreach (FileStream stream in _held)
        {
            stream.Dispose();
        }

        _held.Clear();
    }

    private static void CheckArmed(string file)
    {
        try
        {
            new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite).Dispose();
            throw new InvalidOperationException($"ContentOpenTrap: a managed open of '{file}' succeeded.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Refused, as armed.
        }

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var startInfo = new ProcessStartInfo("cat")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(file);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("ContentOpenTrap: could not start 'cat'.");
        process.StandardOutput.BaseStream.CopyTo(Stream.Null);
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            throw new InvalidOperationException($"ContentOpenTrap: 'cat' read '{file}' while it was armed.");
        }
    }
}
