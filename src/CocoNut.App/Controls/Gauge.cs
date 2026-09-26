using System.Collections.Specialized;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Media;

namespace CocoNut.App.Controls;

/// <summary>
/// A colored band on a <see cref="Gauge"/>'s scale, e.g. the red "low battery" band of a battery gauge.
/// A plain mutable class (not a record) so it can be instantiated and its properties set from XAML.
/// </summary>
public sealed class GaugeRange
{
    public double Start { get; set; }
    public double End { get; set; }
    public IBrush Brush { get; set; } = Brushes.Gray;
}

/// <summary>
/// A custom-drawn circular dial control, the Avalonia equivalent of WinNUT's <c>UPSVarGauge</c>
/// (a modified <see href="https://github.com/Code-Artist/AGauge">AGauge</see>, MIT licensed): an arc with
/// a minimum/maximum scale, optional colored range bands, tick marks, scale numbers, a needle and a
/// caption/value read-out. Unlike AGauge/UPSVarGauge this control has no WinForms designer surface; it is
/// configured entirely through the styled properties below (see <see cref="GaugePresets"/> for ready-made
/// <see cref="Ranges"/> lists matching WinNUT's six main-window dials).
/// </summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100d);

    /// <summary><see langword="null"/> means "no data": the needle is hidden and the value text reads "—".</summary>
    public static readonly StyledProperty<double?> ValueProperty =
        AvaloniaProperty.Register<Gauge, double?>(nameof(Value));

    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<Gauge, string?>(nameof(Caption));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<Gauge, string?>(nameof(Unit));

    /// <summary>.NET numeric format string used for the value read-out, always with <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<Gauge, string>(nameof(ValueFormat), "0.#");

    /// <summary>Number of labelled ticks on the scale, including both ends (minimum 2).</summary>
    public static readonly StyledProperty<int> MajorTickCountProperty =
        AvaloniaProperty.Register<Gauge, int>(nameof(MajorTickCount), 5);

    /// <summary>Number of unlabelled ticks drawn between each pair of neighbouring major ticks.</summary>
    public static readonly StyledProperty<int> MinorTicksPerMajorProperty =
        AvaloniaProperty.Register<Gauge, int>(nameof(MinorTicksPerMajor), 4);

    /// <summary>Colored bands drawn under the tick marks, e.g. from <see cref="GaugePresets"/>. Empty by default.</summary>
    public static readonly StyledProperty<AvaloniaList<GaugeRange>> RangesProperty =
        AvaloniaProperty.Register<Gauge, AvaloniaList<GaugeRange>>(nameof(Ranges));

    /// <summary><see langword="null"/> (the default) picks a theme-aware brush at render time.</summary>
    public static readonly StyledProperty<IBrush?> NeedleBrushProperty =
        AvaloniaProperty.Register<Gauge, IBrush?>(nameof(NeedleBrush));

    /// <summary>Thickness of the background/range arcs, in DIPs at the control's default 160x160 size (it scales with the control).</summary>
    public static readonly StyledProperty<double> ArcThicknessProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(ArcThickness), 10d);

    /// <summary>Angle of <see cref="Minimum"/> in degrees, clockwise from the positive x-axis (3 o'clock).</summary>
    public static readonly StyledProperty<double> StartAngleProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(StartAngle), 135d);

    /// <summary>Angular span of the scale in degrees, clockwise from <see cref="StartAngle"/>.</summary>
    public static readonly StyledProperty<double> SweepAngleProperty =
        AvaloniaProperty.Register<Gauge, double>(nameof(SweepAngle), 270d);

    private const double DefaultSize = 160d;

    private static readonly IBrush FallbackForeground = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush FallbackTrack = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

    static Gauge()
    {
        AffectsRender<Gauge>(
            MinimumProperty, MaximumProperty, ValueProperty, CaptionProperty, UnitProperty,
            ValueFormatProperty, MajorTickCountProperty, MinorTicksPerMajorProperty, RangesProperty,
            NeedleBrushProperty, ArcThicknessProperty, StartAngleProperty, SweepAngleProperty);
    }

    public Gauge()
    {
        Ranges = [];
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string? Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public string? Unit
    {
        get => GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string ValueFormat
    {
        get => GetValue(ValueFormatProperty);
        set => SetValue(ValueFormatProperty, value);
    }

    public int MajorTickCount
    {
        get => GetValue(MajorTickCountProperty);
        set => SetValue(MajorTickCountProperty, value);
    }

    public int MinorTicksPerMajor
    {
        get => GetValue(MinorTicksPerMajorProperty);
        set => SetValue(MinorTicksPerMajorProperty, value);
    }

    public AvaloniaList<GaugeRange> Ranges
    {
        get => GetValue(RangesProperty);
        set => SetValue(RangesProperty, value);
    }

    public IBrush? NeedleBrush
    {
        get => GetValue(NeedleBrushProperty);
        set => SetValue(NeedleBrushProperty, value);
    }

    public double ArcThickness
    {
        get => GetValue(ArcThicknessProperty);
        set => SetValue(ArcThicknessProperty, value);
    }

    public double StartAngle
    {
        get => GetValue(StartAngleProperty);
        set => SetValue(StartAngleProperty, value);
    }

    public double SweepAngle
    {
        get => GetValue(SweepAngleProperty);
        set => SetValue(SweepAngleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != RangesProperty)
        {
            return;
        }

        if (change.OldValue is AvaloniaList<GaugeRange> oldRanges)
        {
            oldRanges.CollectionChanged -= OnRangesCollectionChanged;
        }

        if (change.NewValue is AvaloniaList<GaugeRange> newRanges)
        {
            newRanges.CollectionChanged += OnRangesCollectionChanged;
        }
    }

    private void OnRangesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? DefaultSize : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? DefaultSize : availableSize.Height;
        double side = Math.Max(0, Math.Min(width, height));
        return new Size(side, side);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        Rect bounds = Bounds;
        double size = Math.Min(bounds.Width, bounds.Height);
        if (size <= 0)
        {
            return;
        }

        double scale = size / DefaultSize;
        var center = new Point(bounds.Width / 2, bounds.Height / 2);
        double radius = size / 2;

        IBrush foreground = GetThemeBrush("SystemControlForegroundBaseHighBrush", FallbackForeground);
        IBrush trackBrush = GetThemeBrush("SystemControlForegroundBaseMediumLowBrush", FallbackTrack);
        IBrush needleBrush = NeedleBrush ?? GetThemeBrush("SystemControlForegroundBaseMediumHighBrush", FallbackForeground);

        double minimum = Math.Min(Minimum, Maximum);
        double maximum = Math.Max(Minimum, Maximum);
        double range = maximum - minimum;

        double arcThickness = Math.Max(1, ArcThickness * scale);
        double arcRadius = radius * 0.70;

        // Background track for the whole scale, then the colored range bands on top of it.
        DrawArc(context, center, arcRadius, StartAngle, SweepAngle, new Pen(trackBrush, arcThickness, lineCap: PenLineCap.Flat));

        foreach (GaugeRange bandRange in Ranges)
        {
            double bandStart = Math.Clamp(bandRange.Start, minimum, maximum);
            double bandEnd = Math.Clamp(bandRange.End, minimum, maximum);
            if (bandEnd == bandStart)
            {
                continue;
            }

            double startAngle = ValueToAngle(bandStart, minimum, range);
            double endAngle = ValueToAngle(bandEnd, minimum, range);
            DrawArc(context, center, arcRadius, startAngle, endAngle - startAngle, new Pen(bandRange.Brush, arcThickness, lineCap: PenLineCap.Flat));
        }

        DrawTicks(context, center, radius, minimum, maximum, range, foreground, scale);

        double? clampedValue = Value is double raw ? Math.Clamp(raw, minimum, maximum) : null;
        if (clampedValue is double v)
        {
            DrawNeedle(context, center, radius, ValueToAngle(v, minimum, range), needleBrush, scale);
        }

        // The needle is clamped to the scale, but the read-out shows the real value (e.g. 0 V input during an outage).
        DrawCaptionAndValue(context, center, radius, Value, foreground, scale);
    }

    private double ValueToAngle(double value, double minimum, double range) => StartAngle + SweepAngle * NormalizedPosition(value, minimum, range);

    private static double NormalizedPosition(double value, double minimum, double range) =>
        Math.Abs(range) < 1e-9 ? 0 : (value - minimum) / range;

    private void DrawTicks(DrawingContext context, Point center, double radius, double minimum, double maximum, double range, IBrush foreground, double scale)
    {
        int majorCount = Math.Max(MajorTickCount, 2);
        int minorPerMajor = Math.Max(MinorTicksPerMajor, 0);
        double majorStep = range / (majorCount - 1);

        double majorOuter = radius * 0.78;
        double majorInner = radius * 0.62;
        double minorOuter = radius * 0.74;
        double minorInner = radius * 0.66;
        double labelRadius = radius * 0.92;

        var majorPen = new Pen(foreground, Math.Max(1, 1.5 * scale));
        var minorPen = new Pen(foreground, Math.Max(1, 1.0 * scale));
        var typeface = Typeface.Default;
        double fontSize = Math.Max(7, 10 * scale);

        for (int i = 0; i < majorCount; i++)
        {
            double value = minimum + majorStep * i;
            double angle = StartAngle + SweepAngle * NormalizedPosition(value, minimum, range);

            context.DrawLine(majorPen, PointOnCircle(center, majorInner, angle), PointOnCircle(center, majorOuter, angle));

            var text = new FormattedText(value.ToString("0.#", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, foreground);
            Point labelPoint = PointOnCircle(center, labelRadius, angle);
            context.DrawText(text, new Point(labelPoint.X - text.Width / 2, labelPoint.Y - text.Height / 2));

            if (i < majorCount - 1 && minorPerMajor > 0)
            {
                double minorStep = majorStep / (minorPerMajor + 1);
                for (int j = 1; j <= minorPerMajor; j++)
                {
                    double minorValue = value + minorStep * j;
                    double minorAngle = StartAngle + SweepAngle * NormalizedPosition(minorValue, minimum, range);
                    context.DrawLine(minorPen, PointOnCircle(center, minorInner, minorAngle), PointOnCircle(center, minorOuter, minorAngle));
                }
            }
        }
    }

    private static void DrawNeedle(DrawingContext context, Point center, double radius, double angleDegrees, IBrush needleBrush, double scale)
    {
        double needleLength = radius * 0.60;
        double hubRadius = Math.Max(2, radius * 0.06);

        Point tip = PointOnCircle(center, needleLength, angleDegrees);
        var pen = new Pen(needleBrush, Math.Max(1.5, 3 * scale), lineCap: PenLineCap.Round);
        context.DrawLine(pen, center, tip);
        context.DrawEllipse(needleBrush, null, center, hubRadius, hubRadius);
    }

    private void DrawCaptionAndValue(DrawingContext context, Point center, double radius, double? value, IBrush foreground, double scale)
    {
        var typeface = Typeface.Default;
        var boldTypeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

        string? caption = Caption;
        if (!string.IsNullOrEmpty(caption))
        {
            var captionText = new FormattedText(caption, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, Math.Max(7, 10 * scale), foreground);
            // Below the hub, in the gap between the ends of the scale, so the needle never crosses it.
            var captionPoint = new Point(center.X - captionText.Width / 2, center.Y + radius * 0.22 - captionText.Height / 2);
            context.DrawText(captionText, captionPoint);
        }

        // The read-out uses the UI culture (e.g. "13,2 V" in German); the scale labels stay invariant.
        string valueString = value is double v ? v.ToString(ValueFormat, CultureInfo.CurrentCulture) : "—";
        if (!string.IsNullOrEmpty(Unit) && value is not null)
        {
            valueString = $"{valueString} {Unit}";
        }

        var valueText = new FormattedText(valueString, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            boldTypeface, Math.Max(9, 13 * scale), foreground);
        var valuePoint = new Point(center.X - valueText.Width / 2, center.Y + radius * 0.52 - valueText.Height / 2);
        context.DrawText(valueText, valuePoint);
    }

    private IBrush GetThemeBrush(string resourceKey, IBrush fallback) =>
        this.TryFindResource(resourceKey, out object? resource) && resource is IBrush brush ? brush : fallback;

    /// <summary>
    /// Draws an arc as a sequence of chords each spanning at most 179°, so <see cref="StreamGeometryContext.ArcTo"/>
    /// never needs the "large arc" flag and a full 360° sweep does not degenerate into a single zero-length arc.
    /// </summary>
    private static void DrawArc(DrawingContext context, Point center, double radius, double startAngleDegrees, double sweepAngleDegrees, IPen pen)
    {
        if (radius <= 0 || Math.Abs(sweepAngleDegrees) < 0.01)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(PointOnCircle(center, radius, startAngleDegrees), isFilled: false);

            const double MaxChunkDegrees = 179d;
            double remaining = sweepAngleDegrees;
            double angle = startAngleDegrees;
            while (Math.Abs(remaining) > 0.01)
            {
                double chunk = Math.Sign(remaining) * Math.Min(Math.Abs(remaining), MaxChunkDegrees);
                angle += chunk;
                geometryContext.ArcTo(PointOnCircle(center, radius, angle), new Size(radius, radius), 0,
                    isLargeArc: false, SweepDirection.Clockwise);
                remaining -= chunk;
            }

            geometryContext.EndFigure(isClosed: false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
