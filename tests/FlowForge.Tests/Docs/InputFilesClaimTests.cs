using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// Rename and image nodes change files in the input folder itself, so copy mode cannot keep the originals and no
/// write is confined to a target directory. The documents must say so and never promise otherwise.
/// When sources become read-only during a run, the "keeps originals" wording can return and this check changes with it.
/// </summary>
public class InputFilesClaimTests
{
    private const string Warning = "Work on a copy.";

    private static readonly string[] Documents = ["README.md", "docs/USER_GUIDE.md", "samples/README.md"];

    private static readonly Regex[] FalseClaims =
    [
        new(@"keeps?\s+(the\s+)?originals", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)),
        new(@"inside\s+(its|their|the|a)\s+target\s+director(y|ies)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)),
    ];

    private readonly ITestOutputHelper _output;

    public InputFilesClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Docs_warn_that_input_files_change_and_never_promise_kept_originals()
    {
        var mismatches = new List<string>();
        int scanned = 0;

        foreach (string document in Documents)
        {
            scanned++;
            string text = File.ReadAllText(RepoRoot.Combine(document.Split('/')));
            foreach (Match match in FalseClaims.SelectMany(claim => claim.Matches(text)))
            {
                int line = text.AsSpan(0, match.Index).Count('\n') + 1;
                mismatches.Add($"{document}:{line}: says '{match.Value}', but rename and image nodes change files in the input folder");
            }

            if (!text.Contains(Warning, StringComparison.Ordinal))
            {
                mismatches.Add($"{document}:1: no '{Warning}' warning");
            }
        }

        var registry = NodeRegistry.CreateDefault(NullLoggerFactory.Instance);
        string[] typeKeys = registry.GetRegisteredTypeKeys().ToArray();
        foreach (string typeKey in typeKeys)
        {
            foreach (ConfigField field in registry.GetConfigSchema(typeKey).Where(f => !string.IsNullOrEmpty(f.Description)))
            {
                scanned++;
                foreach (Match match in FalseClaims.SelectMany(claim => claim.Matches(field.Description!)))
                {
                    mismatches.Add($"{typeKey}.{field.Key}: description says '{match.Value}', but rename and image nodes change files in the input folder");
                }
            }
        }

        ClaimCheck.Report(_output, "input-files", scanned, Documents.Length + typeKeys.Length, mismatches);
    }
}
