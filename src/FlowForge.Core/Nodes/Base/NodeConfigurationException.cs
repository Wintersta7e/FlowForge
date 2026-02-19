namespace FlowForge.Core.Nodes.Base;

public class NodeConfigurationException : Exception
{
    public NodeConfigurationException(string message) : base(message) { }
    public NodeConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
