using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace FlowForge.UI.Views;

/// <summary>
/// Molten Works pipe path. Shares its cubic control points with
/// <see cref="MwMercuryDroplet"/> via <see cref="MwGeometry.GetMidpointControls"/>
/// so beads ride exactly on the rendered curve. The shared
/// <see cref="MwGeometry.IsFinite(Point)"/> validator rejects non-finite
/// endpoints at the property boundary on both ends of the curve.
/// </summary>
public sealed class MwPipeConnection : Shape
{
    public static readonly StyledProperty<Point> SourceProperty =
        AvaloniaProperty.Register<MwPipeConnection, Point>(nameof(Source), validate: MwGeometry.IsFinite);

    public static readonly StyledProperty<Point> TargetProperty =
        AvaloniaProperty.Register<MwPipeConnection, Point>(nameof(Target), validate: MwGeometry.IsFinite);

    static MwPipeConnection()
    {
        AffectsGeometry<MwPipeConnection>(SourceProperty, TargetProperty);
    }

    public Point Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Point Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override Geometry? CreateDefiningGeometry()
    {
        Point source = Source;
        Point target = Target;
        (Point cp1, Point cp2) = MwGeometry.GetMidpointControls(source, target);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(source, false);
            context.CubicBezierTo(cp1, cp2, target);
        }

        return geometry;
    }
}
