using FlowForge.Core.Execution;
using FlowForge.Tests.Helpers;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// Image nodes write their results into a private run folder under <see cref="PipelineRunner.DefaultStagingRoot"/>.
/// The user guide must name it for both of its branches: the temp folder on Windows, the per-user data folder elsewhere.
/// </summary>
public class StagingFolderClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public StagingFolderClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guide_names_the_staging_folder_on_each_platform()
    {
        var mismatches = new List<string>();
        string? root = PipelineRunner.DefaultStagingRoot;
        string? parent = root is null ? null : Path.GetDirectoryName(root);
        if (root is null || parent is null)
        {
            mismatches.Add("PipelineRunner.DefaultStagingRoot: no staging root on this machine");
            ClaimCheck.Report(_output, "staging-folder", 0, 2, mismatches);
            return;
        }

        // The code's own last two segments, spelled as the guide gives each platform's folder.
        string app = Path.GetFileName(parent);
        string runs = Path.GetFileName(root);
        string[] named = [$"%TEMP%\\{app}\\{runs}", $"{app}/{runs}"];

        string text = File.ReadAllText(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        foreach (string folder in named.Where(f => !text.Contains($"`{f}`", StringComparison.Ordinal)))
        {
            mismatches.Add($"{GuidePath}:1: does not name the staging folder `{folder}`");
        }

        string baseFolder = OperatingSystem.IsWindows()
            ? Path.GetTempPath()
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(parent) ?? string.Empty), Path.TrimEndingDirectorySeparator(baseFolder), StringComparison.Ordinal))
        {
            mismatches.Add($"PipelineRunner.DefaultStagingRoot: '{root}' is not under '{baseFolder}', the folder the guide names for this platform");
        }

        ClaimCheck.Report(_output, "staging-folder", named.Length, 2, mismatches);
    }
}
