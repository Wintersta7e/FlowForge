using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace FlowForge.UI.Views;

/// <summary>
/// A mercury bead that rides along <see cref="MwPipeConnection"/>'s cubic at a
/// driven <see cref="Progress"/> (clamped to 0..1). Positions itself by writing
/// Canvas.Left / Canvas.Top from <see cref="ArrangeOverride"/>; renders a single
/// cached radial-gradient ellipse for the mercury → molten halo look.
/// </summary>
public sealed class MwMercuryDroplet : Control
{
    private const double OuterRadius = 11;
    private const double BoxSize = OuterRadius * 2;
    private static readonly Point LocalCenter = new(OuterRadius, OuterRadius);

    public static readonly StyledProperty<Point> SourceProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, Point>(nameof(Source), validate: MwGeometry.IsFinite);

    public static readonly StyledProperty<Point> TargetProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, Point>(nameof(Target), validate: MwGeometry.IsFinite);

    // Reject non-finite Progress at the property boundary; Math.Clamp(NaN, 0, 1)
    // returns NaN, which would propagate into Canvas.SetLeft / SetTop.
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<MwMercuryDroplet, double>(nameof(Progress), validate: MwGeometry.IsFinite);

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
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF2, 0xD8, 0xDD, 0xE8), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xD0, 0x80), 0.30));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0xFF, 0x7A, 0x1A), 0.60));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0x7A, 0x1A), 1.00));
        return brush.ToImmutable();
    }

    static MwMercuryDroplet()
    {
        // Box is constant 22×22; only the canvas position changes, so invalidate
        // arrange (not render) when the curve or progress changes.
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
        Point pt = ComputeBezierPoint();
        Canvas.SetLeft(this, pt.X - OuterRadius);
        Canvas.SetTop(this, pt.Y - OuterRadius);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        context.DrawEllipse(DropletBrush, null, LocalCenter, OuterRadius, OuterRadius);
    }

    private Point ComputeBezierPoint()
    {
        Point source = Source;
        Point target = Target;
        (Point cp1, Point cp2) = MwGeometry.GetMidpointControls(source, target);

        // Upstream animation drivers can overshoot at keyframe boundaries.
        double t = Math.Clamp(Progress, 0.0, 1.0);
        double u = 1.0 - t;
        double uu = u * u;
        double tt = t * t;
        double w0 = uu * u;
        double w1 = 3 * uu * t;
        double w2 = 3 * u * tt;
        double w3 = tt * t;

        double x = (w0 * source.X) + (w1 * cp1.X) + (w2 * cp2.X) + (w3 * target.X);
        double y = (w0 * source.Y) + (w1 * cp1.Y) + (w2 * cp2.Y) + (w3 * target.Y);
        return new Point(x, y);
    }
}
