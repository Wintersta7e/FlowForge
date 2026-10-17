using System.Text.Json;
using FlowForge.Core.Execution;
using FlowForge.Core.Nodes.Base;
using FlowForge.Tests.Helpers;
using FlowForge.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Docs;

/// <summary>
/// A number field refuses text that is not a whole number and says under the box which number the pipeline keeps.
/// The user guide must quote that message as the field writes it.
/// </summary>
public class NumberFieldClaimTests
{
    private const string GuidePath = "docs/USER_GUIDE.md";

    private readonly ITestOutputHelper _output;

    public NumberFieldClaimTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Guide_quotes_the_refused_number_message()
    {
        ConfigField width = NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetConfigSchema("ImageResize")
            .Single(field => string.Equals(field.Key, "width", StringComparison.Ordinal));
        var field = new ConfigFieldViewModel(width, new Dictionary<string, JsonElement>(StringComparer.Ordinal), new StubDialogService());
        field.Value = "12";
        field.Value = "12x";

        var mismatches = new List<string>();
        string text = File.ReadAllText(RepoRoot.Combine("docs", "USER_GUIDE.md"));
        if (field.ErrorText is null)
        {
            mismatches.Add("ConfigFieldViewModel: '12x' in a number field gave no message");
        }
        else if (!text.Contains($"`{field.ErrorText}`", StringComparison.Ordinal))
        {
            mismatches.Add($"{GuidePath}:1: does not quote the refused-number message `{field.ErrorText}`");
        }

        ClaimCheck.Report(_output, "number-field", 1, 1, mismatches);
    }
}
