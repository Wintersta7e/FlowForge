using System.Text;
using System.Text.RegularExpressions;
using FlowForge.Core.Execution;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

[Collection(VisualCollection.Name)]
public sealed partial class FrameManifestTests
{
    private readonly ITestOutputHelper _output;

    public FrameManifestTests(ITestOutputHelper output) => _output = output;

    // The gate compares this list with the frames the run wrote, so a frame that was
    // declared but never captured, or captured but never declared, fails the gate.
    [Fact]
    public async Task Manifest_lists_every_declared_frame()
    {
        string manifest = Path.Combine(VisualSession.FrameDirectory, "expected-frames.txt");
        await File.WriteAllTextAsync(
            manifest,
            string.Concat(VisualFrames.Ids.Select(id => id + "\n")),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var typeKeys = NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetRegisteredTypeKeys()
            .ToHashSet(StringComparer.Ordinal);
        var inspectorIds = VisualFrames.Ids
            .Where(id => id.StartsWith(VisualFrames.InspectorPrefix, StringComparison.Ordinal))
            .ToList();
        var numberedIds = VisualFrames.Ids.Where(id => SectionNumberedId().IsMatch(id)).ToList();
        _output.WriteLine($"manifest={manifest}");
        _output.WriteLine($"frames-declared={VisualFrames.Ids.Count}");
        _output.WriteLine($"inspector-ids={inspectorIds.Count}");
        _output.WriteLine($"published={VisualFrames.Published.Count}");
        _output.WriteLine($"numbered-ids={numberedIds.Count}");

        VisualFrames.Ids.Should().NotBeEmpty();
        inspectorIds.Select(id => id[VisualFrames.InspectorPrefix.Length..])
            .Should().OnlyContain(key => typeKeys.Contains(key), "an inspector frame names a registered node type");
        VisualFrames.Published.Should().OnlyContain(id => VisualFrames.IsDeclared(id), "a published frame must be declared");
        numberedIds.Should().BeEmpty("a frame id names what the frame shows, not a document section");
    }

    [GeneratedRegex(@"^(c|s)\d+-", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SectionNumberedId();
}
