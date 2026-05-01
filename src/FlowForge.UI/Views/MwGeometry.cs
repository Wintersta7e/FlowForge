using Avalonia;

namespace FlowForge.UI.Views;

/// <summary>
/// Shared geometry primitives for the custom pipe / bead pair so both ends
/// of the cubic agree on shape and on the same class of non-finite input.
/// </summary>
internal static class MwGeometry
{
    public static bool IsFinite(double value) => double.IsFinite(value);

    public static bool IsFinite(Point value) => double.IsFinite(value.X) && double.IsFinite(value.Y);

    public static bool IsFinite(Size value) => double.IsFinite(value.Width) && double.IsFinite(value.Height);

    /// <summary>
    /// Control points for the simple midpoint cubic between source and target:
    /// cp1 sits horizontally midway, vertically aligned with source; cp2 the
    /// same midway-X, aligned with target. Sharing this between the rendered
    /// pipe and the mercury bead ensures the bead rides exactly on the curve.
    /// </summary>
    public static (Point Cp1, Point Cp2) GetMidpointControls(Point source, Point target)
    {
        double midX = (source.X + target.X) / 2.0;
        return (new Point(midX, source.Y), new Point(midX, target.Y));
    }
}
