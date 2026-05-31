using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryItemViewModel : ViewModelBase
{
    public string TypeKey { get; }
    public string DisplayName { get; }

    /// <summary>Short operation code, e.g. "SHP.01".</summary>
    public string Code { get; }

    /// <summary>Operation subtitle, e.g. "folder · input".</summary>
    public string Sub { get; }

    /// <summary>Category icon-well base fill brush.</summary>
    public IBrush MwBaseBrush { get; }

    /// <summary>Category accent / neon brush for code label and accents.</summary>
    public IBrush MwNeonBrush { get; }

    /// <summary>Forge-themed icon geometry (pit/mold/die/chisel/…).</summary>
    public Geometry? MwIconGeometry { get; }

    public NodeLibraryItemViewModel(string typeKey, string displayName)
    {
        TypeKey = typeKey;
        DisplayName = displayName;

        MwOpsMap.OpMeta meta = MwOpsMap.Get(typeKey);
        Code = meta.Code;
        Sub = meta.Sub;
        MwOpsMap.CategoryKeys keys = MwOpsMap.KeysFor(meta.Category);
        MwBaseBrush = ThemeHelper.GetBrush(keys.Base, "#3a1808");
        MwNeonBrush = ThemeHelper.GetBrush(keys.Neon, "#ff7a1a");
        MwIconGeometry = ThemeHelper.GetGeometry(MwOpsMap.IconKey(meta.Icon));
    }
}
