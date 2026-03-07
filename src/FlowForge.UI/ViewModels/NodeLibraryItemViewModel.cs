namespace FlowForge.UI.ViewModels;

public class NodeLibraryItemViewModel : ViewModelBase
{
    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Icon { get; }

    public NodeLibraryItemViewModel(string typeKey, string displayName, string icon = "")
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Icon = icon;
    }
}
