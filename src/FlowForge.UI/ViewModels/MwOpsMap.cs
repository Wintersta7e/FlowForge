using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FlowForge.UI.ViewModels;

/// <summary>
/// Molten Works metadata for each node type: a short code (e.g. "SHP.01"),
/// a subtitle (e.g. "folder · input"), a category bucket (src/snk/shp/flt/hea/met),
/// an icon name, and the resource keys that drive the per-category brushes.
/// </summary>
internal static class MwOpsMap
{
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct OpMeta(string Code, string Sub, string Category, string Icon);

    /// <summary>Theme-resource keys for a category's brushes and glow color.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct CategoryKeys(string Neon, string Base, string Glow, string GlowColor);

    private static readonly Dictionary<string, OpMeta> Entries = new(StringComparer.Ordinal)
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

    private static readonly Dictionary<string, CategoryKeys> CategoryKeyMap = new(StringComparer.Ordinal)
    {
        ["src"] = new("MwSrc", "MwSrcBase", "MwSrcGlow", "MwSrcGlowColor"),
        ["snk"] = new("MwSnk", "MwSnkBase", "MwSnkGlow", "MwSnkGlowColor"),
        ["shp"] = new("MwShp", "MwShpBase", "MwShpGlow", "MwShpGlowColor"),
        ["flt"] = new("MwFlt", "MwFltBase", "MwFltGlow", "MwFltGlowColor"),
        ["hea"] = new("MwHea", "MwHeaBase", "MwHeaGlow", "MwHeaGlowColor"),
        ["met"] = new("MwMet", "MwMetBase", "MwMetGlow", "MwMetGlowColor"),
    };

    private static readonly CategoryKeys FallbackKeys = new("MwMolten", "MwPanel", "MwMoltenHi", "MwMoltenHiColor");

    public static OpMeta Get(string typeKey) =>
        Entries.TryGetValue(typeKey, out OpMeta meta)
            ? meta
            : new OpMeta(typeKey.ToUpperInvariant(), string.Empty, "hea", "Cog");

    /// <summary>Resource key for the icon geometry (e.g. "MwIcon.Pit").</summary>
    public static string IconKey(string icon) => "MwIcon." + icon;

    /// <summary>Theme-resource keys for the given category bucket.</summary>
    public static CategoryKeys KeysFor(string category) =>
        CategoryKeyMap.TryGetValue(category, out CategoryKeys keys) ? keys : FallbackKeys;
}
