using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FlowForge.UI.Views;

/// <summary>
/// A molten mercury bead that rides along a cubic-bezier pipe at a driven
/// <see cref="Progress"/> (clamped to 0..1). Positions itself by writing
/// Canvas.Left / Canvas.Top on itself so its local coords are reliable, then
/// paints a single ellipse with a cached radial-gradient brush — mercury
/// core → moltenHi ring → molten halo → transparent rim — in one pass.
/// The bezier matches <see cref="MwPipeConnection"/> exactly so the bead
/// tracks the rendered pipe:
///   mx = (source.X + target.X) / 2
///   cp1 = (mx, source.Y),  cp2 = (mx, target.Y)
/// </summary>
public sealed class MwMercuryDroplet : Control
{
    private const double OuterRadius = 11;
    private const double BoxSize = OuterRadius * 2;

    public static readonly StyledProperty<Point> SourceProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, Point>(nameof(Source), validate: IsFinite);

    public static readonly StyledProperty<Point> TargetProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, Point>(nameof(Target), validate: IsFinite);

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, double>(nameof(Progress), validate: IsFinite);

    // Reject NaN / ±Infinity at the property boundary so the bezier math never
    // has to defend against non-finite coordinates. Math.Clamp(NaN, 0, 1)
    // returns NaN, which would propagate into Canvas.SetLeft / SetTop.
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinite(Point value) => IsFinite(value.X) && IsFinite(value.Y);

    // Single radial-gradient brush for the whole bead: opaque mercury core
    // fading through moltenHi yellow and molten orange to a transparent rim.
    // Produces the "glowing metal bead" look in one paint instead of three
    // stacked solid-alpha ellipses (which kept compositing to a flat orange
    // disc with no visible mercury core).
    private static readonly IBrush DropletBrush = BuildDropletBrush();

    private static IBrush BuildDropletBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0xD8, 0xDD, 0xE8), 0.00));   // mercury core
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xD0, 0x80), 0.30));   // moltenHi ring
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0xFF, 0x7A, 0x1A), 0.60));   // molten halo
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0x7A, 0x1A), 1.00));   // transparent rim
        return brush.ToImmutable();
    }

    static MwMercuryDroplet()
    {
        // Size is constant (22×22); only the on-canvas position changes, so
        // invalidate arrange only. Render output is independent of these
        // properties (the ellipse is drawn relative to local coords).
        AffectsArrange<MwMercuryDroplet>(SourceProperty, TargetProperty, ProgressProperty);
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

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(BoxSize, BoxSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Place ourselves via Canvas attached properties at the bezier point so
        // our local (0,0) is the top-left of the box centered on that point.
        // Positioning lives in Arrange, not Measure, because measured size is
        // constant while position depends on animated Source/Target/Progress.
        Point pt = ComputeBezierPoint();
        Canvas.SetLeft(this, pt.X - OuterRadius);
        Canvas.SetTop(this, pt.Y - OuterRadius);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        var center = new Point(OuterRadius, OuterRadius);
        context.DrawEllipse(DropletBrush, null, center, OuterRadius, OuterRadius);
    }

    private Point ComputeBezierPoint()
    {
        Point source = Source;
        Point target = Target;

        double sourceX = source.X;
        double sourceY = source.Y;
        double targetX = target.X;
        double targetY = target.Y;
        double midX = (sourceX + targetX) / 2.0;

        // Clamp to curve domain — upstream animation drivers can overshoot at
        // keyframe boundaries, which would send the bead off the pipe.
        double t = Math.Clamp(Progress, 0.0, 1.0);
        double u = 1.0 - t;

        // Cubic bezier: B(t) = u³·P0 + 3·u²·t·P1 + 3·u·t²·P2 + t³·P3
        double x =
            (u * u * u * sourceX)
            + (3 * u * u * t * midX)
            + (3 * u * t * t * midX)
            + (t * t * t * targetX);

        double y =
            (u * u * u * sourceY)
            + (3 * u * u * t * sourceY)
            + (3 * u * t * t * targetY)
            + (t * t * t * targetY);

        return new Point(x, y);
    }
}
