namespace FlowForge.UI.ViewModels;

public class NodeLibraryItemViewModel : ViewModelBase
{
    public string TypeKey { get; }
    public string DisplayName { get; }

    public NodeLibraryItemViewModel(string typeKey, string displayName)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
    }
}
