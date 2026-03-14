using System.Collections.Generic;

namespace FlowForge.UI.ViewModels;

internal static class NodeIconMap
{
    public static readonly Dictionary<string, string> Icons = new()
    {
        ["FolderInput"] = "\U0001F4C1",
        ["RenamePattern"] = "\u270E",
        ["RenameRegex"] = ".*",
        ["RenameAddAffix"] = "+a",
        ["Filter"] = "\U0001F50D",
        ["Sort"] = "\u21C5",
        ["ImageResize"] = "\U0001F4F8",
        ["ImageConvert"] = "\U0001F3A8",
        ["ImageCompress"] = "\U0001F4E6",
        ["MetadataExtract"] = "\U0001F4C4",
        ["FolderOutput"] = "\U0001F4E5",
    };
}
