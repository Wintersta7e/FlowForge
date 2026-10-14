using System.Diagnostics;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Creates the file and directory links tests need. A link that cannot be created fails the
/// test with the missing privilege named; it never skips it.
/// </summary>
internal static class LinkFactory
{
    private const string PrivilegeHint =
        "Creating a symbolic link needs SeCreateSymbolicLinkPrivilege (run elevated or enable Developer Mode).";

    public static void CreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"{PrivilegeHint} {ex.Message}", ex);
        }
    }

    public static void CreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException($"{PrivilegeHint} {ex.Message}", ex);
        }
    }

    /// <summary>
    /// A junction on Windows, which needs no privilege; a directory symlink elsewhere.
    /// </summary>
    public static async Task CreateDirectoryLinkAsync(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            CreateDirectorySymlink(linkPath, targetPath);
            return;
        }

        // A UNC working directory makes cmd.exe warn and fall back, so run it from the temp folder.
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("cmd.exe did not start.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await stdout;
        string error = await stderr;

        if (process.ExitCode != 0 || new DirectoryInfo(linkPath).LinkTarget == null)
        {
            throw new InvalidOperationException(
                $"mklink /J '{linkPath}' -> '{targetPath}' failed (exit code {process.ExitCode}): {output} {error}");
        }
    }
}
