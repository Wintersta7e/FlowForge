using System.Reflection;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The one owner of frame ids. Every public string constant here is a declared frame;
/// a capture of any other id fails, and the frame manifest lists exactly these.
/// </summary>
internal static class VisualFrames
{
    public const string Launch = "c7-01-launch";

    internal const string InspectorPrefix = "c7-04-inspector-";

    public static IReadOnlyList<string> Ids { get; } = typeof(VisualFrames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal)
        .ToList();

    // Frames another change copies into the repository's screenshots; none yet.
    public static IReadOnlySet<string> Published { get; } = new HashSet<string>(StringComparer.Ordinal);

    public static string Inspector(string typeKey) => InspectorPrefix + typeKey;

    public static bool IsDeclared(string id) => Ids.Contains(id, StringComparer.Ordinal);
}
