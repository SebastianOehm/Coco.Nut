using Avalonia.Collections;
using Avalonia.Media;

namespace CocoNut.App.Controls;

/// <summary>
/// Builds <see cref="GaugeRange"/> lists for <see cref="Gauge.Ranges"/> equivalent to the six UPS dials in
/// WinNUT's main window (<c>WinNUT.vb</c> / <c>WinNUT.Designer.vb</c>: <c>AG_InV</c>, <c>AG_OutV</c>,
/// <c>AG_InF</c>, <c>AG_BattV</c>, <c>AG_Load</c>; the load gauge's secondary "Value2" watts read-out is
/// promoted here to its own <see cref="Power"/> gauge since <see cref="Gauge"/> has a single <see cref="Gauge.Value"/>).
/// </summary>
/// <remarks>
/// WinNUT's <c>UPSVarGauge.RenderDefaultArc</c> paints the arc with a continuous red-to-green
/// <see cref="LinearGradientBrush"/> rather than discrete bands, using a <c>GradientOrientation</c> to
/// decide which end is red. <see cref="Gauge"/> only supports discrete <see cref="Gauge.Ranges"/>, so that
/// gradient is approximated here with red/yellow/green bands that preserve WinNUT's semantics for each
/// gauge (documented per method below), given the calibration minimum/maximum for that gauge
/// (<c>CocoNut.Core.Settings.CalibrationSettings</c>).
/// </remarks>
public static class GaugePresets
{
    private static readonly IBrush DangerBrush = new SolidColorBrush(Color.Parse("#D32F2F"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly IBrush SafeBrush = new SolidColorBrush(Color.Parse("#2E7D32"));

    /// <summary>
    /// Input voltage (WinNUT <c>AG_InV</c>; <c>CalibrationSettings.InputVoltageMin</c>/<c>Max</c>).
    /// WinNUT's <c>GradientOrientation.BottomToTop</c> paints the bottom of the dial red and the top
    /// green; since the 135°..45° sweep has both ends (minimum and maximum) at the bottom and the middle
    /// value at the top, that reads as "too low or too high is bad, the middle of the calibrated range is
    /// nominal" - reproduced here as five bands: red, yellow, green, yellow, red.
    /// </summary>
    public static AvaloniaList<GaugeRange> InputVoltage(double min, double max) => CenterIsSafe(min, max);

    /// <summary>Output voltage (WinNUT <c>AG_OutV</c>; <c>CalibrationSettings.OutputVoltageMin</c>/<c>Max</c>). Same shape as <see cref="InputVoltage"/>.</summary>
    public static AvaloniaList<GaugeRange> OutputVoltage(double min, double max) => CenterIsSafe(min, max);

    /// <summary>Input frequency (WinNUT <c>AG_InF</c>; <c>CalibrationSettings.InputFrequencyMin</c>/<c>Max</c>). Same shape as <see cref="InputVoltage"/>.</summary>
    public static AvaloniaList<GaugeRange> InputFrequency(double min, double max) => CenterIsSafe(min, max);

    /// <summary>Battery voltage (WinNUT <c>AG_BattV</c>; <c>CalibrationSettings.BatteryVoltageMin</c>/<c>Max</c>). Same shape as <see cref="InputVoltage"/>.</summary>
    public static AvaloniaList<GaugeRange> BatteryVoltage(double min, double max) => CenterIsSafe(min, max);

    /// <summary>
    /// Load percent (WinNUT <c>AG_Load</c>; <c>CalibrationSettings.LoadMin</c>/<c>Max</c>). WinNUT's
    /// <c>GradientOrientation.RightToLeft</c> paints the arc's right side (the high-value end, since load
    /// increases from the bottom-left start to the bottom-right end) red and the left side green - the
    /// higher the load, the worse. Reproduced as ascending green/yellow/red bands.
    /// </summary>
    public static AvaloniaList<GaugeRange> Load(double min, double max) => AscendingIsDanger(min, max);

    /// <summary>
    /// Output power in watts. WinNUT shows this as the load gauge's secondary value (<c>Value2</c>, no
    /// dedicated dial or calibration field); Coco.Nut gives it its own gauge colored the same
    /// ascending-is-worse way as <see cref="Load"/>. There being no calibration setting for it, callers
    /// should pass the UPS's nominal/rated power as <paramref name="max"/>.
    /// </summary>
    public static AvaloniaList<GaugeRange> Power(double min, double max) => AscendingIsDanger(min, max);

    /// <summary>Five bands (red/yellow/green/yellow/red): both extremes of [<paramref name="min"/>, <paramref name="max"/>] are danger, the middle is safe.</summary>
    private static AvaloniaList<GaugeRange> CenterIsSafe(double min, double max)
    {
        double range = max - min;
        if (range <= 0)
        {
            return new AvaloniaList<GaugeRange> { new() { Start = min, End = max, Brush = SafeBrush } };
        }

        double a = min + range * 0.2;
        double b = min + range * 0.4;
        double c = min + range * 0.6;
        double d = min + range * 0.8;
        return new AvaloniaList<GaugeRange>
        {
            new() { Start = min, End = a, Brush = DangerBrush },
            new() { Start = a, End = b, Brush = WarningBrush },
            new() { Start = b, End = c, Brush = SafeBrush },
            new() { Start = c, End = d, Brush = WarningBrush },
            new() { Start = d, End = max, Brush = DangerBrush },
        };
    }

    /// <summary>Three bands (green/yellow/red): higher values in [<paramref name="min"/>, <paramref name="max"/>] are worse.</summary>
    private static AvaloniaList<GaugeRange> AscendingIsDanger(double min, double max)
    {
        double range = max - min;
        if (range <= 0)
        {
            return new AvaloniaList<GaugeRange> { new() { Start = min, End = max, Brush = WarningBrush } };
        }

        double a = min + range / 3;
        double b = min + range * 2 / 3;
        return new AvaloniaList<GaugeRange>
        {
            new() { Start = min, End = a, Brush = SafeBrush },
            new() { Start = a, End = b, Brush = WarningBrush },
            new() { Start = b, End = max, Brush = DangerBrush },
        };
    }
}
