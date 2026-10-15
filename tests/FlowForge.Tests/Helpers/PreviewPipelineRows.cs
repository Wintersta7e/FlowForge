using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Every shipped sample (<c>sample/&lt;stem&gt;</c>) and template (<c>template/&lt;id&gt;</c>), plus one chain
/// through every node type (<see cref="AllNodeChain"/>), loaded against one fixture tree: inputs read
/// <c>&lt;root&gt;/input</c>, outputs write <c>&lt;root&gt;/output</c>.
/// </summary>
public static class PreviewPipelineRows
{
    public const string AllNodeChain = "chain/all-nodes";

    private const string SamplePrefix = "sample/";
    private const string TemplatePrefix = "template/";

    public static IReadOnlyList<string> Names()
    {
        string samplesDir = RepoRoot.Combine("samples");
        string[] samples = Directory.Exists(samplesDir)
            ? Directory.GetFiles(samplesDir, "*.ffpipe").Select(Path.GetFileNameWithoutExtension)
                .OfType<string>().Order(StringComparer.Ordinal).ToArray()
            : [];
        if (samples.Length == 0)
        {
            throw new InvalidOperationException($"No .ffpipe sample found under '{samplesDir}'.");
        }

        return samples.Select(stem => SamplePrefix + stem)
            .Concat(PipelineTemplateLibrary.Templates.Select(t => TemplatePrefix + t.Id))
            .ToArray();
    }

    public static TheoryData<string> SamplesAndTemplates()
    {
        var data = new TheoryData<string>();
        foreach (string name in Names())
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>The samples and templates, then <see cref="AllNodeChain"/>.</summary>
    public static TheoryData<string> PreviewRows()
    {
        TheoryData<string> data = SamplesAndTemplates();
        data.Add(AllNodeChain);
        return data;
    }

    /// <summary>Loads a row with its folder paths set before any node is configured.</summary>
    public static async Task<PipelineGraph> LoadAsync(string row, string root)
    {
        if (string.Equals(row, AllNodeChain, StringComparison.Ordinal))
        {
            return BuildAllNodeChain(root);
        }

        PipelineGraph graph = row.StartsWith(SamplePrefix, StringComparison.Ordinal)
            ? await PipelineSerializer.LoadAsync(RepoRoot.Combine("samples", row[SamplePrefix.Length..] + ".ffpipe"))
            : PipelineTemplateLibrary.CreateFromTemplate(row[TemplatePrefix.Length..]);
        foreach (NodeDefinition node in graph.Nodes)
        {
            if (string.Equals(node.TypeKey, "FolderInput", StringComparison.Ordinal))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement(Path.Combine(root, "input"));
            }
            else if (string.Equals(node.TypeKey, "FolderOutput", StringComparison.Ordinal))
            {
                node.Config["path"] = JsonSerializer.SerializeToElement(Path.Combine(root, "output"));
            }
        }

        return graph;
    }

    /// <summary>
    /// Three noise images (over 500 bytes each), two text files (under 50 bytes each) and a subfolder.
    /// </summary>
    public static void CreateFixture(string input)
    {
        CreateNoise(Path.Combine(input, "holiday.jpg"), seed: 1);
        CreateNoise(Path.Combine(input, "scan.png"), seed: 2);
        File.WriteAllText(Path.Combine(input, "notes.txt"), "notes");
        CreateNoise(Path.Combine(input, "sub", "photo2.jpg"), seed: 3);
        File.WriteAllText(Path.Combine(input, "sub", "readme.txt"), "readme");
    }

    /// <summary>Every node type in one chain; Filter, Sort and MetadataExtract read sizes and dates.</summary>
    private static PipelineGraph BuildAllNodeChain(string root) =>
        PipelineBuilder.New("All nodes")
            .AddSource("FolderInput", new { path = Path.Combine(root, "input"), recursive = true })
            .AddTransform("Filter", new { conditions = new[] { new { field = "size", @operator = "greaterthan", value = "500" } } })
            .AddTransform("Sort", new { field = "modifiedat", direction = "asc" })
            .AddTransform("MetadataExtract", new { keys = new[] { "File:SizeBytes", "File:ModifiedAt" } })
            .AddTransform("RenamePattern", new { pattern = "{name}{ext}" })
            .AddTransform("RenameRegex", new { pattern = "photo", replacement = "picture" })
            .AddTransform("RenameAddAffix", new { prefix = "web_" })
            .AddTransform("ImageResize", new { width = 32 })
            .AddTransform("ImageCompress", new { quality = 80 })
            .AddTransform("ImageConvert", new { format = "png" })
            .AddOutput("FolderOutput", new { path = Path.Combine(root, "output") })
            .Build();

    private static void CreateNoise(string path, int seed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(64, 64);
        uint state = (uint)seed;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                state = (state * 1_664_525) + 1_013_904_223;
                image[x, y] = new Rgba32((byte)(state >> 24), (byte)(state >> 16), (byte)(state >> 8));
            }
        }

        image.Save(path);
    }
}
