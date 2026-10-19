using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CocoNut.App.Controls;

/// <summary>
/// A custom-drawn line chart of one UPS metric over a time range (no charting package, like <see cref="Gauge"/>):
/// grid lines with value labels on the left, time labels along the bottom, the series as a line that breaks at
/// missing values and at gaps in the recording, and a hover read-out (vertical crosshair snapped to the nearest
/// point with its local time and value). All the scaling and splitting logic is in <see cref="HistoryChartMath"/>.
/// A series without any value is drawn as a short caption plus <see cref="NoDataText"/> instead of an empty plot.
/// </summary>
public sealed class HistoryChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<HistoryChartPoint>?> PointsProperty =
        AvaloniaProperty.Register<HistoryChart, IReadOnlyList<HistoryChartPoint>?>(nameof(Points));

    /// <summary>Start of the x-axis. The axis spans [<see cref="RangeStart"/>, <see cref="RangeEnd"/>] even where there is no data.</summary>
    public static readonly StyledProperty<DateTimeOffset> RangeStartProperty =
        AvaloniaProperty.Register<HistoryChart, DateTimeOffset>(nameof(RangeStart));

    public static readonly StyledProperty<DateTimeOffset> RangeEndProperty =
        AvaloniaProperty.Register<HistoryChart, DateTimeOffset>(nameof(RangeEnd));

    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<HistoryChart, string?>(nameof(Caption));

    public static readonly StyledProperty<string?> UnitProperty =
        AvaloniaProperty.Register<HistoryChart, string?>(nameof(Unit));

    /// <summary>.NET numeric format string for axis labels and the hover value, applied with the current culture.</summary>
    public static readonly StyledProperty<string> ValueFormatProperty =
        AvaloniaProperty.Register<HistoryChart, string>(nameof(ValueFormat), "0.#");

    /// <summary>Fixed lower bound of the y-axis (e.g. 0), or <see langword="null"/> to scale to the data.</summary>
    public static readonly StyledProperty<double?> MinimumProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(Minimum));

    /// <summary>Fixed upper bound of the y-axis (e.g. 100 for percent), or <see langword="null"/> to scale to the data.</summary>
    public static readonly StyledProperty<double?> MaximumProperty =
        AvaloniaProperty.Register<HistoryChart, double?>(nameof(Maximum));

    /// <summary>Already-localized text shown when the series has no value in the range.</summary>
    public static readonly StyledProperty<string?> NoDataTextProperty =
        AvaloniaProperty.Register<HistoryChart, string?>(nameof(NoDataText));

    /// <summary><see langword="null"/> (the default) uses the theme's accent color.</summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<HistoryChart, IBrush?>(nameof(LineBrush));

    private const double ChartHeight = 190d;
    private const double NoDataHeight = 58d;
    private const double DefaultWidth = 480d;
    private const double CaptionHeight = 24d;
    private const double AxisLabelHeight = 22d;
    private const double RightPadding = 14d;

    private static readonly IBrush FallbackText = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly IBrush FallbackGrid = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
    private static readonly IBrush FallbackLine = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly IBrush FallbackPanel = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));

    private SeriesInfo _series = SeriesInfo.Empty;
    private double? _hoverX;

    // Refreshed by TryGetPlot for the current size and data; Render and the hover hit test both read them.
    private ChartAxis _axis = new(0, 1, [0, 1]);
    private int _timeTickBudget = 6;

    static HistoryChart()
    {
        AffectsRender<HistoryChart>(
            PointsProperty, RangeStartProperty, RangeEndProperty, CaptionProperty, UnitProperty, ValueFormatProperty,
            MinimumProperty, MaximumProperty, NoDataTextProperty, LineBrushProperty);
        AffectsMeasure<HistoryChart>(PointsProperty);
    }

    public HistoryChart()
    {
        // Grid, text and accent brushes come from the theme; redraw when the light/dark variant changes.
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<HistoryChartPoint>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public DateTimeOffset RangeStart
    {
        get => GetValue(RangeStartProperty);
        set => SetValue(RangeStartProperty, value);
    }

    public DateTimeOffset RangeEnd
    {
        get => GetValue(RangeEndProperty);
        set => SetValue(RangeEndProperty, value);
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

    public double? Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double? Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public string? NoDataText
    {
        get => GetValue(NoDataTextProperty);
        set => SetValue(NoDataTextProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>Index into <see cref="Points"/> of the point the hover read-out currently shows, or -1 when there is none.</summary>
    internal int HoveredIndex => _hoverX is double x && TryGetPlot(out var plot, out var range)
        ? HistoryChartMath.FindNearest(_series.Points, HistoryChartMath.UnmapX(x, range.Start, range.End, plot.Left, plot.Width))
        : -1;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PointsProperty)
        {
            _series = SeriesInfo.Create(Points);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? DefaultWidth : availableSize.Width;
        double height = _series.HasData ? ChartHeight : NoDataHeight;
        if (!double.IsInfinity(availableSize.Height))
        {
            height = Math.Min(height, availableSize.Height);
        }

        return new Size(Math.Max(0, width), Math.Max(0, height));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hoverX = e.GetPosition(this).X;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverX = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = new Rect(Bounds.Size);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Transparent but present, so the whole control takes part in pointer hit testing for the hover read-out.
        context.FillRectangle(Brushes.Transparent, bounds);

        IBrush text = GetThemeBrush("SystemControlForegroundBaseHighBrush", FallbackText);
        IBrush mutedText = GetThemeBrush("SystemControlForegroundBaseMediumBrush", FallbackText);
        IBrush grid = GetThemeBrush("SystemControlForegroundBaseLowBrush", FallbackGrid);

        DrawCaption(context, text);

        if (!_series.HasData)
        {
            if (!string.IsNullOrEmpty(NoDataText))
            {
                var noData = CreateText(NoDataText, 12, mutedText);
                context.DrawText(noData, new Point(0, CaptionHeight + 2));
            }

            return;
        }

        if (!TryGetPlot(out Rect plot, out var range))
        {
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        var axis = _axis;
        var gridPen = new Pen(grid, 1);

        // Value axis: a grid line and a right-aligned label per tick.
        foreach (double tick in axis.Ticks)
        {
            double y = Math.Round(HistoryChartMath.MapY(tick, axis.Minimum, axis.Maximum, plot.Top, plot.Height)) + 0.5;
            context.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var label = CreateText(tick.ToString(ValueFormat, culture), 11, mutedText);
            context.DrawText(label, new Point(plot.Left - 6 - label.Width, y - label.Height / 2));
        }

        // Time axis: a grid line and a centered label per tick, kept inside the control.
        var timeTicks = HistoryChartMath.TimeTicks(range.Start, range.End, _timeTickBudget, TimeZoneInfo.Local, out TimeSpan step);
        var labelFormat = HistoryChartMath.ChooseLabelFormat(range.End - range.Start, step);
        foreach (var tick in timeTicks)
        {
            double x = Math.Round(HistoryChartMath.MapX(tick, range.Start, range.End, plot.Left, plot.Width)) + 0.5;
            context.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var label = CreateText(HistoryChartMath.FormatTimeLabel(tick, labelFormat, culture, TimeZoneInfo.Local), 11, mutedText);
            double labelX = Math.Clamp(x - label.Width / 2, 0, Math.Max(0, bounds.Width - label.Width));
            context.DrawText(label, new Point(labelX, plot.Bottom + 5));
        }

        IBrush lineBrush = LineBrush ?? GetAccentBrush();
        using (context.PushClip(plot.Inflate(2)))
        {
            DrawSeries(context, plot, range, axis, lineBrush);
        }

        DrawHover(context, plot, range, axis, lineBrush, text, mutedText, culture);
    }

    /// <summary>Computes the plot rectangle (and refreshes the axis and tick budget) for the current size and data.</summary>
    private bool TryGetPlot(out Rect plot, out (DateTimeOffset Start, DateTimeOffset End) range)
    {
        plot = default;
        range = default;
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (!_series.HasData || width <= 0 || height <= CaptionHeight + AxisLabelHeight + 20)
        {
            return false;
        }

        range = RangeEnd > RangeStart ? (RangeStart, RangeEnd) : (_series.FirstTime, _series.LastTime > _series.FirstTime ? _series.LastTime : _series.FirstTime.AddMinutes(1));

        double plotTop = CaptionHeight;
        double plotHeight = height - CaptionHeight - AxisLabelHeight;
        int valueTicks = (int)Math.Clamp(plotHeight / 40, 2, 6);
        _axis = HistoryChartMath.ComputeAxis(_series.Minimum, _series.Maximum, Minimum, Maximum, valueTicks);

        // At least four digits wide, so stacked charts with short and long value labels still line up their time axes.
        double labelWidth = CreateText("8888", 11, FallbackText).Width;
        foreach (double tick in _axis.Ticks)
        {
            labelWidth = Math.Max(labelWidth, CreateText(tick.ToString(ValueFormat, CultureInfo.CurrentCulture), 11, FallbackText).Width);
        }

        double left = Math.Ceiling(labelWidth) + 10;
        double plotWidth = width - left - RightPadding;
        if (plotWidth < 40)
        {
            return false;
        }

        // The widest label the range can produce decides how many time ticks fit.
        var widest = (range.End - range.Start) > TimeSpan.FromHours(24) ? TimeLabelFormat.DateTime : TimeLabelFormat.Time;
        double timeLabelWidth = CreateText(
            HistoryChartMath.FormatTimeLabel(range.Start, widest, CultureInfo.CurrentCulture, TimeZoneInfo.Local), 11, FallbackText).Width;
        _timeTickBudget = Math.Max(2, (int)(plotWidth / (timeLabelWidth + 18)));

        plot = new Rect(left, plotTop, plotWidth, plotHeight);
        return true;
    }

    private void DrawCaption(DrawingContext context, IBrush text)
    {
        string? caption = Caption;
        if (string.IsNullOrEmpty(caption))
        {
            return;
        }

        string title = string.IsNullOrEmpty(Unit) ? caption : $"{caption} ({Unit})";
        var typeface = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
        var formatted = new FormattedText(title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 13, text);
        context.DrawText(formatted, new Point(0, 2));
    }

    private void DrawSeries(DrawingContext context, Rect plot, (DateTimeOffset Start, DateTimeOffset End) range, ChartAxis axis, IBrush lineBrush)
    {
        var points = _series.Points;
        var pen = new Pen(lineBrush, 1.75, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        foreach (var (start, count) in _series.Segments)
        {
            if (count == 1)
            {
                context.DrawEllipse(lineBrush, null, ToPixel(points[start], plot, range, axis), 2.25, 2.25);
                continue;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext geometryContext = geometry.Open())
            {
                geometryContext.BeginFigure(ToPixel(points[start], plot, range, axis), isFilled: false);
                for (int i = start + 1; i < start + count; i++)
                {
                    geometryContext.LineTo(ToPixel(points[i], plot, range, axis));
                }

                geometryContext.EndFigure(isClosed: false);
            }

            context.DrawGeometry(null, pen, geometry);
        }
    }

    private void DrawHover(
        DrawingContext context, Rect plot, (DateTimeOffset Start, DateTimeOffset End) range, ChartAxis axis,
        IBrush lineBrush, IBrush text, IBrush mutedText, CultureInfo culture)
    {
        if (_hoverX is not double hoverX || hoverX < plot.Left || hoverX > plot.Right)
        {
            return;
        }

        var points = _series.Points;
        int index = HistoryChartMath.FindNearest(points, HistoryChartMath.UnmapX(hoverX, range.Start, range.End, plot.Left, plot.Width));
        if (index < 0)
        {
            return;
        }

        var point = ToPixel(points[index], plot, range, axis);
        if (point.X < plot.Left - 1 || point.X > plot.Right + 1)
        {
            return;
        }

        context.DrawLine(new Pen(mutedText, 1, dashStyle: DashStyle.Dash), new Point(point.X, plot.Top), new Point(point.X, plot.Bottom));
        context.DrawEllipse(lineBrush, new Pen(GetThemeBrush("SystemControlBackgroundAltHighBrush", FallbackPanel), 1.5), point, 4, 4);

        string valueText = points[index].Value!.Value.ToString(ValueFormat, culture);
        if (!string.IsNullOrEmpty(Unit))
        {
            valueText = $"{valueText} {Unit}";
        }

        var timeLine = CreateText(TimeZoneInfo.ConvertTime(points[index].Timestamp, TimeZoneInfo.Local).ToString("G", culture), 11, mutedText);
        var valueLine = new FormattedText(valueText, culture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 12, text);

        const double PadX = 8;
        const double PadY = 5;
        double boxWidth = Math.Max(timeLine.Width, valueLine.Width) + 2 * PadX;
        double boxHeight = timeLine.Height + valueLine.Height + 2 * PadY;
        double boxX = point.X + 10 + boxWidth <= plot.Right ? point.X + 10 : point.X - 10 - boxWidth;
        double boxY = Math.Clamp(point.Y - boxHeight / 2, plot.Top, Math.Max(plot.Top, plot.Bottom - boxHeight));
        var box = new Rect(boxX, boxY, boxWidth, boxHeight);

        context.DrawRectangle(
            GetThemeBrush("SystemControlBackgroundChromeMediumLowBrush", FallbackPanel),
            new Pen(GetThemeBrush("SystemControlForegroundBaseLowBrush", FallbackGrid), 1),
            box, 4, 4);
        context.DrawText(timeLine, new Point(box.X + PadX, box.Y + PadY));
        context.DrawText(valueLine, new Point(box.X + PadX, box.Y + PadY + timeLine.Height));
    }

    private static Point ToPixel(HistoryChartPoint point, Rect plot, (DateTimeOffset Start, DateTimeOffset End) range, ChartAxis axis) => new(
        HistoryChartMath.MapX(point.Timestamp, range.Start, range.End, plot.Left, plot.Width),
        HistoryChartMath.MapY(point.Value!.Value, axis.Minimum, axis.Maximum, plot.Top, plot.Height));

    private static FormattedText CreateText(string text, double size, IBrush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size, brush);

    // The theme variant must be passed explicitly: without it the lookup resolves the light-theme value, which made
    // text black on black in dark mode (see Gauge).
    private IBrush GetThemeBrush(string resourceKey, IBrush fallback) =>
        this.TryFindResource(resourceKey, ActualThemeVariant, out object? resource) && resource is IBrush brush ? brush : fallback;

    // A lighter accent on the dark background keeps the line readable; Fluent defines both shades per variant.
    private IBrush GetAccentBrush()
    {
        string key = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "SystemAccentColorLight1" : "SystemAccentColor";
        return this.TryFindResource(key, ActualThemeVariant, out object? resource) && resource is Color color
            ? new SolidColorBrush(color)
            : FallbackLine;
    }

    /// <summary>What <see cref="Render"/> needs to know about the points, computed once per <see cref="Points"/> change.</summary>
    private sealed record SeriesInfo(
        IReadOnlyList<HistoryChartPoint> Points,
        IReadOnlyList<(int Start, int Count)> Segments,
        bool HasData,
        double Minimum,
        double Maximum,
        DateTimeOffset FirstTime,
        DateTimeOffset LastTime)
    {
        public static readonly SeriesInfo Empty = new([], [], false, 0, 0, default, default);

        public static SeriesInfo Create(IReadOnlyList<HistoryChartPoint>? points)
        {
            if (points is null || points.Count == 0)
            {
                return Empty;
            }

            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            DateTimeOffset first = default;
            DateTimeOffset last = default;
            bool any = false;
            foreach (var point in points)
            {
                if (point.Value is not double value || double.IsNaN(value) || double.IsInfinity(value))
                {
                    continue;
                }

                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                if (!any)
                {
                    first = point.Timestamp;
                    any = true;
                }

                last = point.Timestamp;
            }

            if (!any)
            {
                return Empty with { Points = points };
            }

            var threshold = HistoryChartMath.ComputeGapThreshold(points);
            return new SeriesInfo(points, HistoryChartMath.SplitSegments(points, threshold), true, minimum, maximum, first, last);
        }
    }
}
