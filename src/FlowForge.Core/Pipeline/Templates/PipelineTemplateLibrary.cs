using System.Text.Json;

namespace FlowForge.Core.Pipeline.Templates;

public static class PipelineTemplateLibrary
{
    public static IReadOnlyList<PipelineTemplate> Templates { get; } = new[]
    {
        PhotoImportByDate(),
        BatchSequentialRename(),
        ImageWebExport(),
        BulkImageCompress()
    };

    public static PipelineGraph CreateFromTemplate(string templateId)
    {
        PipelineTemplate template = Templates.FirstOrDefault(t => t.Id == templateId)
            ?? throw new ArgumentException($"Unknown template: '{templateId}'");

        // Deep clone by serializing and deserializing
        string json = JsonSerializer.Serialize(template.Graph);
        PipelineGraph clone = JsonSerializer.Deserialize<PipelineGraph>(json)!;

        // Assign new IDs so each instance is unique
        clone = new PipelineGraph
        {
            Name = clone.Name,
            Nodes = clone.Nodes.Select(n => new NodeDefinition
            {
                Id = Guid.NewGuid(),
                TypeKey = n.TypeKey,
                Position = n.Position,
                Config = new Dictionary<string, JsonElement>(n.Config)
            }).ToList(),
            Connections = new List<Connection>()
        };

        // Re-wire linear connections
        for (int i = 0; i < clone.Nodes.Count - 1; i++)
        {
            clone.Connections.Add(new Connection
            {
                FromNode = clone.Nodes[i].Id,
                ToNode = clone.Nodes[i + 1].Id
            });
        }

        return clone;
    }

    private static PipelineTemplate PhotoImportByDate()
    {
        PipelineGraph graph = BuildLinearGraph(
            "Photo Import — Rename by EXIF Date",
            Node("FolderInput", new { path = "", recursive = true, filter = "*.jpg;*.jpeg;*.png;*.heic" }),
            Node("MetadataExtract", new { keys = new[] { "EXIF:DateTaken" } }),
            Node("RenamePattern", new { pattern = "{meta:EXIF:DateTaken}_{name}{ext}" }),
            Node("FolderOutput", new { path = "", mode = "copy" })
        );

        return new PipelineTemplate("photo-import-by-date", "Photo Import — Rename by EXIF Date", graph);
    }

    private static PipelineTemplate BatchSequentialRename()
    {
        PipelineGraph graph = BuildLinearGraph(
            "Batch Sequential Rename",
            Node("FolderInput", new { path = "", filter = "*" }),
            Node("RenamePattern", new { pattern = "{counter:000}{ext}", startIndex = 1 }),
            Node("FolderOutput", new { path = "", mode = "copy" })
        );

        return new PipelineTemplate("batch-sequential-rename", "Batch Sequential Rename", graph);
    }

    private static PipelineTemplate ImageWebExport()
    {
        PipelineGraph graph = BuildLinearGraph(
            "Image Web Export (Resize + Convert to WebP)",
            Node("FolderInput", new { path = "", filter = "*.jpg;*.png" }),
            Node("Filter", new
            {
                conditions = new[]
                {
                    new { field = "extension", @operator = "equals", value = ".jpg" }
                }
            }),
            Node("ImageResize", new { width = 1920, mode = "max" }),
            Node("ImageConvert", new { format = "webp" }),
            Node("FolderOutput", new { path = "", mode = "copy" })
        );

        return new PipelineTemplate("image-web-export", "Image Web Export (Resize + Convert to WebP)", graph);
    }

    private static PipelineTemplate BulkImageCompress()
    {
        PipelineGraph graph = BuildLinearGraph(
            "Bulk Image Compress",
            Node("FolderInput", new { path = "", filter = "*.jpg" }),
            Node("Filter", new
            {
                conditions = new[]
                {
                    new { field = "extension", @operator = "equals", value = ".jpg" }
                }
            }),
            Node("ImageCompress", new { quality = 80 }),
            Node("FolderOutput", new { path = "", mode = "copy" })
        );

        return new PipelineTemplate("bulk-image-compress", "Bulk Image Compress", graph);
    }

    private static NodeDefinition Node(string typeKey, object config)
    {
        string json = JsonSerializer.Serialize(config);
        JsonDocument doc = JsonDocument.Parse(json);
        Dictionary<string, JsonElement> configDict = doc.RootElement
            .EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());

        return new NodeDefinition
        {
            TypeKey = typeKey,
            Config = configDict
        };
    }

    private static PipelineGraph BuildLinearGraph(string name, params NodeDefinition[] nodes)
    {
        var graph = new PipelineGraph { Name = name };
        graph.Nodes.AddRange(nodes);

        for (int i = 0; i < nodes.Length - 1; i++)
        {
            graph.Connections.Add(new Connection
            {
                FromNode = nodes[i].Id,
                ToNode = nodes[i + 1].Id
            });
        }

        return graph;
    }
}

public record PipelineTemplate(string Id, string Name, PipelineGraph Graph);
