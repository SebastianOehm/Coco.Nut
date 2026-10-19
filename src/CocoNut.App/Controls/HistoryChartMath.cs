using System.Globalization;
using System.Text.RegularExpressions;

namespace CocoNut.App.Controls;

/// <summary>How the x-axis tick labels of a <see cref="HistoryChart"/> are written.</summary>
internal enum TimeLabelFormat
{
    /// <summary>Time of day only (ranges up to 24 hours).</summary>
    Time,
    /// <summary>Short date and time (longer ranges with ticks closer than a day).</summary>
    DateTime,
    /// <summary>Short date only (ticks a day or more apart).</summary>
    Date,
}

/// <summary>A y-axis: its bounds and the tick values (all within the bounds) that get a grid line and a label.</summary>
internal sealed record ChartAxis(double Minimum, double Maximum, IReadOnlyList<double> Ticks);

/// <summary>
/// The pure geometry and scaling behind <see cref="HistoryChart"/> (tick values, gap handling, value-to-pixel
/// mapping, hit testing, label formats), kept free of any drawing so it can be unit tested without Avalonia.
/// </summary>
internal static partial class HistoryChartMath
{
    /// <summary>Two consecutive points are never connected when further apart than this, whatever the series' spacing.</summary>
    public static readonly TimeSpan GapFloor = TimeSpan.FromSeconds(30);

    /// <summary>A point further than this many median spacings from its predecessor starts a new line segment.</summary>
    public const double GapSpacingFactor = 3d;

    private static readonly TimeSpan[] TimeSteps =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2),
        TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(5), TimeSpan.FromDays(7), TimeSpan.FromDays(14),
    ];

    // ---- Value axis -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tick values at multiples of a "nice" step (1, 2, 5 times a power of ten) covering [<paramref name="minimum"/>,
    /// <paramref name="maximum"/>], aiming for about <paramref name="targetCount"/> ticks. The first tick is at or
    /// below <paramref name="minimum"/>, the last at or above <paramref name="maximum"/>.
    /// </summary>
    public static IReadOnlyList<double> NiceTicks(double minimum, double maximum, int targetCount)
    {
        if (maximum < minimum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        double step = NiceStep(maximum - minimum, targetCount);
        double first = Math.Floor(minimum / step + 1e-9) * step;
        var ticks = new List<double>();
        for (int i = 0; ; i++)
        {
            double tick = Math.Round(first + i * step, 10);
            ticks.Add(tick);
            if (tick >= maximum - step * 1e-9 || i > 1000)
            {
                break;
            }
        }

        return ticks;
    }

    /// <summary>
    /// Builds the y-axis for data spanning [<paramref name="dataMinimum"/>, <paramref name="dataMaximum"/>]. A
    /// <paramref name="fixedMinimum"/>/<paramref name="fixedMaximum"/> is used as is; the other side is padded a
    /// little and rounded out to a nice tick (without crossing zero for non-negative data).
    /// </summary>
    public static ChartAxis ComputeAxis(double dataMinimum, double dataMaximum, double? fixedMinimum, double? fixedMaximum, int targetCount)
    {
        double low = dataMinimum;
        double high = dataMaximum;
        if (high - low < 1e-12)
        {
            // A flat series: open a window around it so the line sits mid-chart instead of on an edge.
            double pad = Math.Max(Math.Abs(low) * 0.05, 1d);
            low -= pad;
            high += pad;
        }
        else
        {
            double pad = (high - low) * 0.08;
            low -= pad;
            high += pad;
        }

        if (dataMinimum >= 0 && low < 0)
        {
            low = 0;
        }

        if (fixedMinimum is double fixedLow)
        {
            low = fixedLow;
        }

        if (fixedMaximum is double fixedHigh)
        {
            high = fixedHigh;
        }

        if (high <= low)
        {
            high = low + 1;
        }

        var ticks = NiceTicks(low, high, targetCount);
        double axisMinimum = fixedMinimum ?? ticks[0];
        double axisMaximum = fixedMaximum ?? ticks[^1];
        var inside = ticks.Where(t => t >= axisMinimum - 1e-9 && t <= axisMaximum + 1e-9).ToList();
        return new ChartAxis(axisMinimum, axisMaximum, inside);
    }

    private static double NiceStep(double range, int targetCount)
    {
        if (range <= 0 || double.IsNaN(range) || double.IsInfinity(range))
        {
            return 1;
        }

        double rough = range / Math.Max(1, targetCount - 1);
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double fraction = rough / magnitude;
        double nice = fraction switch
        {
            <= 1.5 => 1,
            <= 3 => 2,
            <= 7 => 5,
            _ => 10,
        };
        return nice * magnitude;
    }

    // ---- Segments and gaps ------------------------------------------------------------------------------------------

    /// <summary>
    /// The largest time between two consecutive points that is still drawn as a connected line:
    /// <see cref="GapSpacingFactor"/> times the median spacing of the non-null points, but at least <see cref="GapFloor"/>.
    /// </summary>
    public static TimeSpan ComputeGapThreshold(IReadOnlyList<HistoryChartPoint> points)
    {
        var spacings = new List<double>();
        HistoryChartPoint? previous = null;
        foreach (var point in points)
        {
            if (point.Value is null)
            {
                continue;
            }

            if (previous is { } before)
            {
                double seconds = (point.Timestamp - before.Timestamp).TotalSeconds;
                if (seconds > 0)
                {
                    spacings.Add(seconds);
                }
            }

            previous = point;
        }

        if (spacings.Count == 0)
        {
            return GapFloor;
        }

        spacings.Sort();
        int mid = spacings.Count / 2;
        double median = spacings.Count % 2 == 1 ? spacings[mid] : (spacings[mid - 1] + spacings[mid]) / 2;
        return TimeSpan.FromSeconds(Math.Max(median * GapSpacingFactor, GapFloor.TotalSeconds));
    }

    /// <summary>
    /// Splits <paramref name="points"/> into runs of consecutive indices that are drawn as one polyline: a null value
    /// ends a run, and so does a time jump larger than <paramref name="gapThreshold"/> between neighbouring points.
    /// Runs are returned as (first index, count); a run of one point is drawn as a dot.
    /// </summary>
    public static IReadOnlyList<(int Start, int Count)> SplitSegments(IReadOnlyList<HistoryChartPoint> points, TimeSpan gapThreshold)
    {
        var segments = new List<(int Start, int Count)>();
        int start = -1;
        DateTimeOffset previousTime = default;

        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point.Value is null)
            {
                Close(i);
                continue;
            }

            if (start >= 0 && point.Timestamp - previousTime > gapThreshold)
            {
                Close(i);
            }

            if (start < 0)
            {
                start = i;
            }

            previousTime = point.Timestamp;
        }

        Close(points.Count);
        return segments;

        void Close(int endExclusive)
        {
            if (start >= 0)
            {
                segments.Add((start, endExclusive - start));
                start = -1;
            }
        }
    }

    // ---- Mapping and hit testing --------------------------------------------------------------------------------------

    /// <summary>Maps a time within [<paramref name="start"/>, <paramref name="end"/>] to x in [<paramref name="left"/>, <paramref name="left"/> + <paramref name="width"/>].</summary>
    public static double MapX(DateTimeOffset time, DateTimeOffset start, DateTimeOffset end, double left, double width)
    {
        double span = (end - start).TotalSeconds;
        return span <= 0 ? left : left + (time - start).TotalSeconds / span * width;
    }

    /// <summary>Inverse of <see cref="MapX"/>.</summary>
    public static DateTimeOffset UnmapX(double x, DateTimeOffset start, DateTimeOffset end, double left, double width) =>
        width <= 0 ? start : start + TimeSpan.FromSeconds((x - left) / width * (end - start).TotalSeconds);

    /// <summary>Maps a value within [<paramref name="minimum"/>, <paramref name="maximum"/>] to y; the maximum is at <paramref name="top"/>.</summary>
    public static double MapY(double value, double minimum, double maximum, double top, double height)
    {
        double span = maximum - minimum;
        return span <= 0 ? top + height : top + height - (value - minimum) / span * height;
    }

    /// <summary>Index of the non-null point closest in time to <paramref name="time"/> (points sorted ascending), or -1 if there is none.</summary>
    public static int FindNearest(IReadOnlyList<HistoryChartPoint> points, DateTimeOffset time)
    {
        int low = 0;
        int high = points.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (points[mid].Timestamp < time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        int before = low - 1;
        while (before >= 0 && points[before].Value is null)
        {
            before--;
        }

        int after = low;
        while (after < points.Count && points[after].Value is null)
        {
            after++;
        }

        if (before < 0)
        {
            return after < points.Count ? after : -1;
        }

        if (after >= points.Count)
        {
            return before;
        }

        return time - points[before].Timestamp <= points[after].Timestamp - time ? before : after;
    }

    // ---- Time axis ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Tick times inside [<paramref name="start"/>, <paramref name="end"/>] at round local clock times (whole minutes,
    /// hours or days), using the smallest step that gives at most <paramref name="maxCount"/> ticks.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> TimeTicks(DateTimeOffset start, DateTimeOffset end, int maxCount, TimeZoneInfo zone, out TimeSpan step)
    {
        step = TimeSteps[^1];
        double rangeSeconds = (end - start).TotalSeconds;
        if (rangeSeconds <= 0)
        {
            return [];
        }

        foreach (var candidate in TimeSteps)
        {
            if (rangeSeconds / candidate.TotalSeconds <= Math.Max(1, maxCount))
            {
                step = candidate;
                break;
            }
        }

        var localStart = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var localEnd = TimeZoneInfo.ConvertTime(end, zone).DateTime;
        var midnight = localStart.Date;
        var first = step < TimeSpan.FromDays(1)
            ? midnight + TimeSpan.FromTicks((long)Math.Ceiling((localStart - midnight).Ticks / (double)step.Ticks) * step.Ticks)
            : (localStart > midnight ? midnight.AddDays(1) : midnight);

        var ticks = new List<DateTimeOffset>();
        for (var local = first; local <= localEnd && ticks.Count < 1000; local += step)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(unspecified))
            {
                continue;
            }

            var tick = new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
            if (tick >= start && tick <= end)
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    /// <summary>Time of day for ranges up to a day; otherwise date and time while ticks are closer than a day, and the date alone beyond.</summary>
    public static TimeLabelFormat ChooseLabelFormat(TimeSpan range, TimeSpan step)
    {
        if (range <= TimeSpan.FromHours(24))
        {
            return TimeLabelFormat.Time;
        }

        return step < TimeSpan.FromDays(1) ? TimeLabelFormat.DateTime : TimeLabelFormat.Date;
    }

    /// <summary>Formats a tick label in the zone's local time and the given culture.</summary>
    public static string FormatTimeLabel(DateTimeOffset time, TimeLabelFormat format, CultureInfo culture, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(time, zone);
        var dateTimeFormat = culture.DateTimeFormat;
        return format switch
        {
            TimeLabelFormat.Time => local.ToString(dateTimeFormat.ShortTimePattern, culture),
            TimeLabelFormat.Date => local.ToString(ShortDateWithoutYear(dateTimeFormat.ShortDatePattern), culture),
            _ => local.ToString(ShortDateWithoutYear(dateTimeFormat.ShortDatePattern) + " " + dateTimeFormat.ShortTimePattern, culture),
        };
    }

    /// <summary>Drops the year (and its separator) from a culture's short date pattern: <c>M/d/yyyy</c> becomes <c>M/d</c>.</summary>
    public static string ShortDateWithoutYear(string shortDatePattern)
    {
        var trimmed = YearPart().Replace(shortDatePattern, string.Empty);
        return trimmed.Length > 0 ? trimmed : shortDatePattern;
    }

    [GeneratedRegex(@"^y+[^dM]*|[^dM]*y+$")]
    private static partial Regex YearPart();
}
