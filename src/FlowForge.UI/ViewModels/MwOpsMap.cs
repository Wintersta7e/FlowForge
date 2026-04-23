using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FlowForge.UI.ViewModels;

/// <summary>
/// Molten Works metadata for each node type: a short code (e.g. "SHP.01"),
/// a subtitle (e.g. "folder · input"), and a category bucket key used to
/// pick category brushes/glows (src, snk, shp, flt, hea, met).
/// </summary>
internal static class MwOpsMap
{
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct OpMeta(string Code, string Sub, string Category, string Icon);

    private static readonly Dictionary<string, OpMeta> Entries = new(System.StringComparer.Ordinal)
    {
        ["FolderInput"] = new("SRC.01", "folder · input", "src", "Pit"),
        ["FolderOutput"] = new("SNK.01", "folder · output", "snk", "Mold"),
        ["RenamePattern"] = new("SHP.01", "{date}_{counter}", "shp", "Die"),
        ["RenameRegex"] = new("SHP.02", "find / replace", "shp", "Chisel"),
        ["RenameAddAffix"] = new("SHP.03", "name.affix", "shp", "Brand"),
        ["Filter"] = new("FLT.01", "keep · drop", "flt", "Sieve"),
        ["Sort"] = new("FLT.02", "order · group", "flt", "Rack"),
        ["ImageResize"] = new("HEA.01", "width · height", "hea", "Mill"),
        ["ImageConvert"] = new("HEA.02", "jpg · png · webp", "hea", "Crucible"),
        ["ImageCompress"] = new("HEA.03", "quality · size", "hea", "Press"),
        ["MetadataExtract"] = new("MET.01", "exif · iptc", "met", "Loupe"),
    };

    public static OpMeta Get(string typeKey) =>
        Entries.TryGetValue(typeKey, out OpMeta meta)
            ? meta
            : new OpMeta(typeKey.ToUpperInvariant(), string.Empty, "hea", "Cog");

    /// <summary>Resource key for the icon geometry (e.g. "MwIcon.Pit").</summary>
    public static string IconKey(string icon) => "MwIcon." + icon;

    /// <summary>
    /// Resource key for the category neon brush (text / accent color).
    /// </summary>
    public static string NeonKey(string category) => category switch
    {
        "src" => "MwSrc",
        "snk" => "MwSnk",
        "shp" => "MwShp",
        "flt" => "MwFlt",
        "hea" => "MwHea",
        "met" => "MwMet",
        _ => "MwMolten",
    };

    /// <summary>
    /// Resource key for the category icon-well base fill color.
    /// </summary>
    public static string BaseKey(string category) => category switch
    {
        "src" => "MwSrcBase",
        "snk" => "MwSnkBase",
        "shp" => "MwShpBase",
        "flt" => "MwFltBase",
        "hea" => "MwHeaBase",
        "met" => "MwMetBase",
        _ => "MwPanel",
    };

    /// <summary>
    /// Resource key for the category outer glow brush.
    /// </summary>
    public static string GlowKey(string category) => category switch
    {
        "src" => "MwSrcGlow",
        "snk" => "MwSnkGlow",
        "shp" => "MwShpGlow",
        "flt" => "MwFltGlow",
        "hea" => "MwHeaGlow",
        "met" => "MwMetGlow",
        _ => "MwMoltenHi",
    };
}
