namespace FlowForge.Core.Pipeline.Templates;

public record PipelineTemplate(string Id, string Name, string ShortName, string Description, PipelineGraph Graph);
