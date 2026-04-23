using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace FlowForge.UI.Views;

/// <summary>
/// Molten Works pipe path that renders the same midpoint cubic used by the
/// concept reference and by <see cref="MwMercuryDroplet"/>.
/// </summary>
public sealed class MwPipeConnection : Shape
{
    // Non-finite endpoints or offsets would corrupt the cubic StreamGeometry
    // and silently draw nothing. Mirror the validator MwMercuryDroplet applies
    // to its own Source/Target so both ends of the shared curve enforce the
    // same boundary contract.
    public static readonly StyledProperty<Point> SourceProperty =
        AvaloniaProperty.Register<MwPipeConnection, Point>(nameof(Source), validate: MwGeometry.IsFinite);

    public static readonly StyledProperty<Point> TargetProperty =
        AvaloniaProperty.Register<MwPipeConnection, Point>(nameof(Target), validate: MwGeometry.IsFinite);

    public static readonly StyledProperty<Size> SourceOffsetProperty =
        AvaloniaProperty.Register<MwPipeConnection, Size>(nameof(SourceOffset), validate: MwGeometry.IsFinite);

    public static readonly StyledProperty<Size> TargetOffsetProperty =
        AvaloniaProperty.Register<MwPipeConnection, Size>(nameof(TargetOffset), validate: MwGeometry.IsFinite);

    static MwPipeConnection()
    {
        AffectsGeometry<MwPipeConnection>(
            SourceProperty,
            TargetProperty,
            SourceOffsetProperty,
            TargetOffsetProperty);
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

    public Size SourceOffset
    {
        get => GetValue(SourceOffsetProperty);
        set => SetValue(SourceOffsetProperty, value);
    }

    public Size TargetOffset
    {
        get => GetValue(TargetOffsetProperty);
        set => SetValue(TargetOffsetProperty, value);
    }

    protected override Geometry? CreateDefiningGeometry()
    {
        Point source = ApplyOffset(Source, SourceOffset);
        Point target = ApplyOffset(Target, TargetOffset);
        double midX = (source.X + target.X) / 2.0;

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(source, false);
            context.CubicBezierTo(
                new Point(midX, source.Y),
                new Point(midX, target.Y),
                target);
        }

        return geometry;
    }

    private static Point ApplyOffset(Point point, Size offset) =>
        new(point.X + offset.Width, point.Y + offset.Height);
}
