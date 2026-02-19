namespace FlowForge.Core.Pipeline;

public class PipelineLoadException : Exception
{
    public PipelineLoadException(string message) : base(message) { }
    public PipelineLoadException(string message, Exception innerException) : base(message, innerException) { }
}
