using Avalonia.Media;

namespace FlowForge.UI.ViewModels;

public class NodeLibraryItemViewModel : ViewModelBase
{
    public string TypeKey { get; }
    public string DisplayName { get; }
    public string Icon { get; }
    public IBrush IconBackground { get; }
    public IBrush IconForeground { get; }

    /// <summary>Short operation code, e.g. "SHP.01".</summary>
    public string Code { get; }

    /// <summary>Operation subtitle, e.g. "folder · input".</summary>
    public string Sub { get; }

    /// <summary>Molten Works category bucket ("src", "snk", "shp", "flt", "hea", "met").</summary>
    public string MwCategory { get; }

    /// <summary>Category icon-well base fill brush.</summary>
    public IBrush MwBaseBrush { get; }

    /// <summary>Category accent / neon brush for code label and accents.</summary>
    public IBrush MwNeonBrush { get; }

    /// <summary>Forge-themed icon geometry (pit/mold/die/chisel/…).</summary>
    public Geometry? MwIconGeometry { get; }

    public NodeLibraryItemViewModel(
        string typeKey,
        string displayName,
        string icon,
        IBrush iconBackground,
        IBrush iconForeground)
    {
        TypeKey = typeKey;
        DisplayName = displayName;
        Icon = icon;
        IconBackground = iconBackground;
        IconForeground = iconForeground;

        MwOpsMap.OpMeta meta = MwOpsMap.Get(typeKey);
        Code = meta.Code;
        Sub = meta.Sub;
        MwCategory = meta.Category;
        MwBaseBrush = ThemeHelper.GetBrush(MwOpsMap.BaseKey(meta.Category), "#3a1808");
        MwNeonBrush = ThemeHelper.GetBrush(MwOpsMap.NeonKey(meta.Category), "#ff7a1a");
        MwIconGeometry = ThemeHelper.GetGeometry(MwOpsMap.IconKey(meta.Icon));
    }
}
