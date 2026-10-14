using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Tests.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Pipeline;

public class PipelineCompatTests
{
    private const string InputId = "0c000000-0000-0000-0000-000000000001";
    private const string ConvertId = "0c000000-0000-0000-0000-000000000002";
    private const string ResizeId = "0c000000-0000-0000-0000-000000000003";
    private const string OutputId = "0c000000-0000-0000-0000-000000000004";

    private const string ResizeVersion1 = $$"""
        {
          "name": "Resize",
          "version": "1.0",
          "nodes": [
            { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } },
            { "id": "{{ResizeId}}", "typeKey": "ImageResize", "config": { "width": 640 } },
            { "id": "{{OutputId}}", "typeKey": "FolderOutput", "config": { "path": "out" } }
          ],
          "connections": [
            { "fromNode": "{{InputId}}", "toNode": "{{ResizeId}}" },
            { "fromNode": "{{ResizeId}}", "toNode": "{{OutputId}}" }
          ]
        }
        """;

    private const string ResizeWithoutVersion = $$"""
        {
          "name": "Resize",
          "nodes": [
            { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } },
            { "id": "{{ResizeId}}", "typeKey": "ImageResize", "config": { "width": 640 } },
            { "id": "{{OutputId}}", "typeKey": "FolderOutput", "config": { "path": "out" } }
          ],
          "connections": [
            { "fromNode": "{{InputId}}", "toNode": "{{ResizeId}}" },
            { "fromNode": "{{ResizeId}}", "toNode": "{{OutputId}}" }
          ]
        }
        """;

    private const string ConvertToTiffVersion1 = $$"""
        {
          "name": "Convert to TIFF",
          "version": "1.0",
          "nodes": [
            { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } },
            { "id": "{{ConvertId}}", "typeKey": "ImageConvert", "config": { "format": "TIFF" } },
            { "id": "{{OutputId}}", "typeKey": "FolderOutput", "config": { "path": "out" } }
          ],
          "connections": [
            { "fromNode": "{{InputId}}", "toNode": "{{ConvertId}}" },
            { "fromNode": "{{ConvertId}}", "toNode": "{{OutputId}}" }
          ]
        }
        """;

    private const string CopyVersion1 = $$"""
        {
          "name": "Copy",
          "version": "1.0",
          "nodes": [
            { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } },
            { "id": "{{OutputId}}", "typeKey": "FolderOutput", "config": { "path": "out" } }
          ],
          "connections": [
            { "fromNode": "{{InputId}}", "toNode": "{{OutputId}}" }
          ]
        }
        """;

    private readonly ITestOutputHelper _output;

    public PipelineCompatTests(ITestOutputHelper output) => _output = output;

    /// <summary>One older-version file per compatibility entry, keyed "code|node type" or "code|graph".</summary>
    internal static IReadOnlyDictionary<string, string> FixtureByEntry { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tiff-output-lzw|ImageConvert"] = ConvertToTiffVersion1,
        ["tiff-output-lzw|ImageResize"] = ResizeVersion1,
        ["output-name-collision-suffix|graph"] = CopyVersion1,
    };

    [Fact]
    public async Task Each_compat_entry_has_a_fixture_that_yields_its_code()
    {
        using var dir = new TempDirectory();
        var failures = new List<string>();
        int covered = 0;

        foreach (CompatEntry entry in CompatTable.Entries)
        {
            string key = $"{entry.Code}|{entry.TypeKey ?? "graph"}";
            if (!FixtureByEntry.TryGetValue(key, out string? json))
            {
                failures.Add($"{key}: no fixture");
                continue;
            }

            PipelineGraph graph = await LoadAsync(dir, json);
            bool yielded = entry.TypeKey is null
                ? graph.LoadNotices.Contains(new CompatNotice(entry.Code, null, null, entry.Message))
                : graph.Nodes
                    .Where(node => string.Equals(node.TypeKey, entry.TypeKey, StringComparison.Ordinal))
                    .Any(node => graph.LoadNotices.Contains(new CompatNotice(entry.Code, node.Id, node.TypeKey, entry.Message)));
            if (yielded)
            {
                covered++;
            }
            else
            {
                failures.Add($"{key}: the fixture yields no notice with that code and node");
            }
        }

        _output.WriteLine($"[compat] covered={covered} entries={CompatTable.Entries.Count}");
        CompatTable.Entries.Should().NotBeEmpty();
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Version_less_file_yields_the_same_notices_as_version_1_0()
    {
        using var dir = new TempDirectory();

        PipelineGraph withVersion = await LoadAsync(dir, ResizeVersion1);
        PipelineGraph withoutVersion = await LoadAsync(dir, ResizeWithoutVersion);

        _output.WriteLine($"notices={withoutVersion.LoadNotices.Count}");
        withoutVersion.LoadNotices.Should().NotBeEmpty();
        withoutVersion.LoadNotices.Select(n => (n.Code, n.NodeId))
            .Should().Equal(withVersion.LoadNotices.Select(n => (n.Code, n.NodeId)));
    }

    [Fact]
    public async Task Unknown_or_non_string_version_is_refused()
    {
        using var dir = new TempDirectory();
        (string VersionJson, string Named)[] cases = [("\"9.9\"", "9.9"), ("2", "2")];

        foreach ((string versionJson, string named) in cases)
        {
            string json = $$"""
                {
                  "name": "Future",
                  "version": {{versionJson}},
                  "nodes": [ { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } } ],
                  "connections": []
                }
                """;

            Func<Task> act = () => LoadAsync(dir, json);

            await act.Should().ThrowAsync<PipelineLoadException>($"version {versionJson} is not one this build reads")
                .WithMessage($"*{named}*");
        }
    }

    [Fact]
    public async Task Current_version_file_has_no_notices()
    {
        using var dir = new TempDirectory();
        string json = $$"""
            {
              "name": "Current",
              "version": "{{KnownVersions.Current}}",
              "nodes": [
                { "id": "{{InputId}}", "typeKey": "FolderInput", "config": { "path": "in" } },
                { "id": "{{ResizeId}}", "typeKey": "ImageResize", "config": { "width": 640 } },
                { "id": "{{OutputId}}", "typeKey": "FolderOutput", "config": { "path": "out" } }
              ],
              "connections": [
                { "fromNode": "{{InputId}}", "toNode": "{{ResizeId}}" },
                { "fromNode": "{{ResizeId}}", "toNode": "{{OutputId}}" }
              ]
            }
            """;

        PipelineGraph graph = await LoadAsync(dir, json);

        graph.LoadNotices.Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_a_loaded_version_1_0_pipeline_writes_the_current_version()
    {
        using var dir = new TempDirectory();
        string path = Path.Combine(dir.Path, "saved.ffpipe");
        await File.WriteAllTextAsync(path, ResizeVersion1);
        PipelineGraph graph = await PipelineSerializer.LoadAsync(path);

        await PipelineSerializer.SaveAsync(graph, path);

        using (var written = JsonDocument.Parse(await File.ReadAllTextAsync(path)))
        {
            written.RootElement.GetProperty("version").GetString().Should().Be(KnownVersions.Current);
        }

        PipelineGraph reloaded = await PipelineSerializer.LoadAsync(path);
        reloaded.LoadNotices.Should().BeEmpty();
    }

    private static async Task<PipelineGraph> LoadAsync(TempDirectory dir, string json)
    {
        string path = Path.Combine(dir.Path, $"{Guid.NewGuid():N}.ffpipe");
        await File.WriteAllTextAsync(path, json);
        return await PipelineSerializer.LoadAsync(path);
    }
}
