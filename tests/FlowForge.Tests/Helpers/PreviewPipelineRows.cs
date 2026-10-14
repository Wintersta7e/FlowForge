using System.Text.Json;
using FlowForge.Core.Pipeline;
using FlowForge.Core.Pipeline.Templates;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FlowForge.Tests.Helpers;

/// <summary>
/// Every shipped sample (<c>sample/&lt;stem&gt;</c>) and template (<c>template/&lt;id&gt;</c>), loaded
/// against one fixture tree: inputs read <c>&lt;root&gt;/input</c>, outputs write <c>&lt;root&gt;/output</c>.
/// </summary>
public static class PreviewPipelineRows
{
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

    /// <summary>Loads a row with its folder paths set before any node is configured.</summary>
    public static async Task<PipelineGraph> LoadAsync(string row, string root)
    {
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

    /// <summary>Two images with real detail, two text files and a subfolder.</summary>
    public static void CreateFixture(string input)
    {
        CreateGradient(Path.Combine(input, "holiday.jpg"));
        CreateGradient(Path.Combine(input, "scan.png"));
        File.WriteAllText(Path.Combine(input, "notes.txt"), "notes");
        CreateGradient(Path.Combine(input, "sub", "photo2.jpg"));
        File.WriteAllText(Path.Combine(input, "sub", "readme.txt"), "readme");
    }

    private static void CreateGradient(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = new Image<Rgba32>(64, 48);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                image[x, y] = new Rgba32((byte)(x * 4), (byte)(y * 5), (byte)((x + y) * 2));
            }
        }

        image.Save(path);
    }
}
