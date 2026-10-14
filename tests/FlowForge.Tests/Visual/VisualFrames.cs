using System.Reflection;
using FlowForge.Core.Execution;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The one owner of frame ids. Every public string constant here is a declared frame, and
/// so is one inspector frame per registered node type; a capture of any other id fails,
/// and the frame manifest lists exactly these.
/// </summary>
internal static class VisualFrames
{
    public const string Launch = "launch";

    public const string LibraryDrag = "library-drag";

    public const string ConnectPending = "connect-pending";

    public const string ConnectDone = "connect-done";

    public const string EditPersists = "edit-persists";

    public const string SelectDeleteUndo = "select-delete-undo";

    public const string MoveStation = "move-station";

    public const string NodeRender = "node-render";

    public const string LogProgress = "log-progress";

    public const string LogRows = "log-rows";

    public const string RealRun = "real-run";

    public const string MinEmpty = "w-min-empty";

    public const string MinIdle = "w-min-idle";

    public const string MinInspector = "w-min-inspector";

    public const string RunDemo = "run-demo";

    public const string BeadsOnPipe = "fx-beads";

    public const string EyesSelectionGlow = "eyes-selection-glow";

    public const string EyesEmptyInspector = "eyes-empty-inspector";

    public const string EyesToolbarRunning = "eyes-toolbar-running";

    public const string EyesLibrarySearch = "eyes-library-search";

    public const string EyesLongTitle = "eyes-long-title";

    public const string EyesFit = "eyes-fit";

    public const string EyesShortcutsDialog = "eyes-shortcuts-dialog";

    public const string EyesRecentFlyout = "eyes-recent-flyout";

    internal const string InspectorPrefix = "inspector-";

    public static IReadOnlyList<string> Ids { get; } = typeof(VisualFrames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Concat(NodeRegistry.CreateDefault(NullLoggerFactory.Instance).GetRegisteredTypeKeys().Select(Inspector))
        .Order(StringComparer.Ordinal)
        .ToList();

    // Frames another change copies into the repository's screenshots; none yet.
    public static IReadOnlySet<string> Published { get; } = new HashSet<string>(StringComparer.Ordinal);

    public static string Inspector(string typeKey) => InspectorPrefix + typeKey;

    public static bool IsDeclared(string id) => Ids.Contains(id, StringComparer.Ordinal);
}
