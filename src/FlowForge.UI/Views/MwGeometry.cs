using Avalonia;

namespace FlowForge.UI.Views;

/// <summary>
/// Boundary validators shared by the custom pipe / bead pair so both ends of
/// the cubic reject the same class of non-finite input. <see cref="MwPipeConnection"/>
/// and <see cref="MwMercuryDroplet"/> consume these from their StyledProperty
/// <c>validate</c> callbacks.
/// </summary>
internal static class MwGeometry
{
    public static bool IsFinite(double value) => double.IsFinite(value);

    public static bool IsFinite(Point value) => double.IsFinite(value.X) && double.IsFinite(value.Y);

    public static bool IsFinite(Size value) => double.IsFinite(value.Width) && double.IsFinite(value.Height);
}
