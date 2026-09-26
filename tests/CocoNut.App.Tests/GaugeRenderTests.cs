using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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

    [AvaloniaFact]
    public void Gauge_in_dark_theme_draws_light_ticks_and_text()
    {
        // Regression: theme brushes were resolved without the theme variant, so ticks, labels, needle and read-out
        // were black on black in dark mode. With Ranges empty, only those theme-coloured parts can be near-white.
        var gauge = new Gauge { Width = 200, Height = 200, Minimum = 0, Maximum = 100, Value = 42, Caption = "Load", Unit = "%" };
        var window = new Window { Content = gauge, Width = 200, Height = 200, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();

        WriteableBitmap frame = window.CaptureRenderedFrame()!;

        Assert.True(CountLightPixels(frame) > 50, "Expected light (theme foreground) pixels on the dark background.");
    }

    private static int CountLightPixels(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var count = 0;
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var i = y * buffer.RowBytes + x * 4; // BGRA/RGBA, 8 bits per channel
                if (bytes[i] > 200 && bytes[i + 1] > 200 && bytes[i + 2] > 200)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
