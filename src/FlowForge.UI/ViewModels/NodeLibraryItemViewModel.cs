using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryItemViewModel : ViewModelBase
{
    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Icon { get; }
    public IBrush IconBackground { get; }
    public IBrush IconForeground { get; }

    public NodeLibraryItemViewModel(string typeKey, string displayName, string icon, IBrush iconBackground, IBrush iconForeground)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Icon = icon;
        IconBackground = iconBackground;
        IconForeground = iconForeground;
    }
}
