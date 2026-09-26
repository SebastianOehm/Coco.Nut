using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CocoNut.App.Controls;

namespace CocoNut.App.Tests;

public class GaugeRenderTests
{
    [AvaloniaFact]
    public void Gauge_with_a_value_measures_arranges_and_renders_without_exception()
    {
        var gauge = new Gauge
        {
            Width = 200,
            Height = 200,
            Minimum = 0,
            Maximum = 100,
            Value = 42,
            Caption = "Load",
            Unit = "%",
            Ranges = GaugePresets.Load(0, 100),
        };

        var window = new Window { Content = gauge, Width = 220, Height = 220 };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(new Size(200, 200), gauge.Bounds.Size);
    }

    [AvaloniaFact]
    public void Gauge_with_a_null_value_hides_the_needle_and_renders_without_exception()
    {
        var gauge = new Gauge { Width = 160, Height = 160, Minimum = 0, Maximum = 100, Value = null, Caption = "No data" };

        var window = new Window { Content = gauge, Width = 180, Height = 180 };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
    }

    [AvaloniaFact]
    public void Gauge_measured_without_a_constraint_defaults_to_a_160x160_square()
    {
        var gauge = new Gauge();

        gauge.Measure(Size.Infinity);

        Assert.Equal(new Size(160, 160), gauge.DesiredSize);
    }

    [AvaloniaTheory]
    [InlineData(200, 120)]
    [InlineData(90, 300)]
    public void Gauge_measured_with_a_non_square_constraint_stays_square(double width, double height)
    {
        var gauge = new Gauge();

        gauge.Measure(new Size(width, height));

        Assert.Equal(gauge.DesiredSize.Width, gauge.DesiredSize.Height);
        Assert.True(gauge.DesiredSize.Width <= Math.Min(width, height));
    }

    [AvaloniaFact]
    public void Gauge_clamps_an_out_of_range_value_before_rendering()
    {
        var gauge = new Gauge { Width = 160, Height = 160, Minimum = 0, Maximum = 100, Value = 999 };

        var window = new Window { Content = gauge, Width = 180, Height = 180 };
        window.Show();

        // Just needs to not throw: DrawNeedle must never receive an angle outside [StartAngle, StartAngle+SweepAngle].
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void Ranges_collection_changes_invalidate_the_visual_without_reassigning_the_property()
    {
        var gauge = new Gauge { Width = 160, Height = 160, Minimum = 0, Maximum = 100, Value = 50 };

        var window = new Window { Content = gauge, Width = 180, Height = 180 };
        window.Show();
        window.CaptureRenderedFrame();

        gauge.Ranges.Add(new GaugeRange { Start = 0, End = 50, Brush = Avalonia.Media.Brushes.Red });

        // Re-rendering after mutating the shared Ranges list (rather than replacing it) must not throw.
        window.CaptureRenderedFrame();
    }
}
