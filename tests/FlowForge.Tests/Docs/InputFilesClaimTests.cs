using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// Rename and image nodes work on private copies and only Folder Output writes to the user's folders, so a run in
/// copy mode leaves the input files unchanged. The documents and Folder Output's mode description must say so, and
/// never again warn that the nodes change files in the input folder.
/// </summary>
public class InputFilesClaimTests
{
    private const string Claim = "Rename and image nodes never change the input folder";

    private const string CopyModeClaim = "copy: leave the input files unchanged";

    private static readonly string[] Documents = ["README.md", "docs/USER_GUIDE.md", "samples/README.md"];

    private static readonly Regex[] StaleClaims =
    [
        new(@"work\s+on\s+a\s+copy", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)),
        new(@"change(s|d)?\s+(files\s+in\s+the\s+input\s+folder|it\s+there)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)),
    ];

    private readonly ITestOutputHelper _output;

    public InputFilesClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Docs_say_a_run_leaves_the_input_files_unchanged()
    {
        var mismatches = new List<string>();
        int scanned = 0;

        foreach (string document in Documents)
        {
            scanned++;
            string text = File.ReadAllText(RepoRoot.Combine(document.Split('/')));
            foreach (Match match in StaleClaims.SelectMany(claim => claim.Matches(text)))
            {
                int line = text.AsSpan(0, match.Index).Count('\n') + 1;
                mismatches.Add($"{document}:{line}: says '{match.Value}', but rename and image nodes leave the input folder unchanged");
            }

            if (!text.Contains(Claim, StringComparison.Ordinal))
            {
                mismatches.Add($"{document}:1: does not say '{Claim}'");
            }
        }

        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        string[] typeKeys = registry.GetRegisteredTypeKeys().ToArray();
        foreach (string typeKey in typeKeys)
        {
            foreach (ConfigField field in registry.GetConfigSchema(typeKey).Where(f => !string.IsNullOrEmpty(f.Description)))
            {
                scanned++;
                foreach (Match match in StaleClaims.SelectMany(claim => claim.Matches(field.Description!)))
                {
                    mismatches.Add($"{typeKey}.{field.Key}: description says '{match.Value}', but rename and image nodes leave the input folder unchanged");
                }
            }
        }

        string? mode = registry.GetConfigSchema("FolderOutput").Single(f => string.Equals(f.Key, "mode", StringComparison.Ordinal)).Description;
        if (mode is null || !mode.StartsWith(CopyModeClaim, StringComparison.Ordinal))
        {
            mismatches.Add($"FolderOutput.mode: description '{mode}' does not start with '{CopyModeClaim}'");
        }

        ClaimCheck.Report(_output, "input-files", scanned, Documents.Length + typeKeys.Length, mismatches);
    }
}
