using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FlowForge.UI.Views;
using FluentAssertions;
using Xunit.Abstractions;

namespace FlowForge.Tests.Visual;

/// <summary>
/// The beads ride the pipe they belong to: each droplet's centre lies on the pipe's rendered
/// stroke, and neither control accepts a non-finite point or progress.
/// </summary>
[Collection(VisualCollection.Name)]
public sealed class BeadGeometryTests
{
    private readonly ITestOutputHelper _output;

    public BeadGeometryTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public void Droplets_ride_the_rendered_pipe()
    {
        using var session = VisualSession.Open(_output);
        var source = new Point(40, 40);
        var target = new Point(440, 200);

        // Thin on purpose: a droplet 3 px off the curve must leave the stroke, which a wide pen would hide.
        var pipe = new MwPipeConnection { Source = source, Target = target, Stroke = Brushes.White, StrokeThickness = 2 };
        MwMercuryDroplet[] droplets = new[] { 0, 0.25, 0.5, 0.75 }
            .Select(progress => new MwMercuryDroplet { Source = source, Target = target, Progress = progress })
            .ToArray();
        var canvas = new Canvas();
        canvas.Children.Add(pipe);
        canvas.Children.AddRange(droplets);
        var fixture = new Window { Width = 480, Height = 240, Content = canvas };
        bool closed = false;
        fixture.Closed += (_, _) => closed = true;
        fixture.Show();
        try
        {
            session.RenderUntil(
                () => pipe.RenderedGeometry is not null
                    && droplets.All(d => !double.IsNaN(Canvas.GetLeft(d)) && !double.IsNaN(Canvas.GetTop(d))),
                "droplets arranged on the pipe");
            using WriteableBitmap frame = session.Capture(VisualFrames.BeadsOnPipe, fixture);

            var pen = new Pen(pipe.Stroke, pipe.StrokeThickness);
            var results = droplets.Select(d =>
            {
                // A droplet places its box's top-left corner on the canvas; its centre is half the box further.
                var centre = new Point(Canvas.GetLeft(d) + (d.DesiredSize.Width / 2), Canvas.GetTop(d) + (d.DesiredSize.Height / 2));
                return (d.Progress, Centre: centre, OnPipe: pipe.RenderedGeometry!.StrokeContains(pen, centre));
            }).ToList();
            foreach ((double progress, Point centre, bool onPipe) in results)
            {
                _output.WriteLine($"progress={progress} centre={centre} on-pipe={onPipe}");
            }

            results.Should().OnlyContain(r => r.OnPipe, "every droplet centre lies on the pipe's rendered stroke");
        }
        finally
        {
            fixture.Close();
            session.RenderUntil(() => closed, "fixture window closed");
        }
    }

    [AvaloniaFact]
    public void Pipe_and_droplet_reject_non_finite_values()
    {
        var pipe = new MwPipeConnection();
        var droplet = new MwMercuryDroplet();
        (string Property, Action<double> Set)[] properties =
        [
            ("pipe Source.X", v => pipe.Source = new Point(v, 0)),
            ("pipe Source.Y", v => pipe.Source = new Point(0, v)),
            ("pipe Target.X", v => pipe.Target = new Point(v, 0)),
            ("pipe Target.Y", v => pipe.Target = new Point(0, v)),
            ("droplet Source.X", v => droplet.Source = new Point(v, 0)),
            ("droplet Source.Y", v => droplet.Source = new Point(0, v)),
            ("droplet Target.X", v => droplet.Target = new Point(v, 0)),
            ("droplet Target.Y", v => droplet.Target = new Point(0, v)),
            ("droplet Progress", v => droplet.Progress = v),
        ];

        int tried = 0;
        int rejected = 0;
        foreach ((string property, Action<double> set) in properties)
        {
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                tried++;
                try
                {
                    set(value);
                    _output.WriteLine($"{property}={value}: accepted");
                }
                catch (ArgumentException ex)
                {
                    rejected++;
                    _output.WriteLine($"{property}={value}: rejected ({ex.Message})");
                }
            }
        }

        _output.WriteLine($"rejected={rejected} tried={tried}");
        rejected.Should().Be(tried, "every non-finite point and progress is refused at the property");
    }
}
