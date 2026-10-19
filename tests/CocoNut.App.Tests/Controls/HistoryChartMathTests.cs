using System.Globalization;
using CocoNut.App.Controls;

namespace CocoNut.App.Tests.Controls;

public class HistoryChartMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static List<HistoryChartPoint> Series(params (double Seconds, double? Value)[] points) =>
        points.Select(p => new HistoryChartPoint(T0.AddSeconds(p.Seconds), p.Value)).ToList();

    // ---- Nice ticks and axis ----------------------------------------------------------------------------------------

    [Fact]
    public void NiceTicks_for_a_percent_range_are_round_and_evenly_spaced()
    {
        var ticks = HistoryChartMath.NiceTicks(0, 100, 5);

        Assert.Equal([0d, 20d, 40d, 60d, 80d, 100d], ticks);
    }

    [Theory]
    [InlineData(228.3, 231.7, 5)]
    [InlineData(0.013, 0.047, 4)]
    [InlineData(-12, 38, 6)]
    [InlineData(0, 1500, 5)]
    public void NiceTicks_cover_the_range_with_a_constant_step(double minimum, double maximum, int target)
    {
        var ticks = HistoryChartMath.NiceTicks(minimum, maximum, target);

        Assert.True(ticks[0] <= minimum + 1e-9);
        Assert.True(ticks[^1] >= maximum - 1e-9);
        Assert.InRange(ticks.Count, 2, target + 4);
        double step = ticks[1] - ticks[0];
        for (int i = 2; i < ticks.Count; i++)
        {
            Assert.Equal(step, ticks[i] - ticks[i - 1], 6);
        }
    }

    [Fact]
    public void ComputeAxis_with_fixed_bounds_uses_them_exactly()
    {
        var axis = HistoryChartMath.ComputeAxis(35, 62, 0, 100, 5);

        Assert.Equal(0, axis.Minimum);
        Assert.Equal(100, axis.Maximum);
        Assert.All(axis.Ticks, tick => Assert.InRange(tick, 0, 100));
        Assert.Contains(0d, axis.Ticks);
        Assert.Contains(100d, axis.Ticks);
    }

    [Fact]
    public void ComputeAxis_leaves_headroom_around_auto_scaled_data()
    {
        var axis = HistoryChartMath.ComputeAxis(226.4, 233.8, null, null, 5);

        Assert.True(axis.Minimum < 226.4);
        Assert.True(axis.Maximum > 233.8);
        Assert.Equal(axis.Minimum, axis.Ticks[0]);
        Assert.Equal(axis.Maximum, axis.Ticks[^1]);
    }

    [Fact]
    public void ComputeAxis_does_not_dip_below_zero_for_non_negative_data()
    {
        var axis = HistoryChartMath.ComputeAxis(0, 40, null, null, 5);

        Assert.Equal(0, axis.Minimum);
        Assert.True(axis.Maximum >= 40);
    }

    [Fact]
    public void ComputeAxis_opens_a_window_around_a_flat_series()
    {
        var axis = HistoryChartMath.ComputeAxis(50, 50, null, null, 5);

        Assert.True(axis.Minimum < 50);
        Assert.True(axis.Maximum > 50);
    }

    [Fact]
    public void ComputeAxis_with_only_a_fixed_minimum_keeps_it_and_scales_the_top()
    {
        var axis = HistoryChartMath.ComputeAxis(120, 480, 0, null, 5);

        Assert.Equal(0, axis.Minimum);
        Assert.True(axis.Maximum >= 480);
    }

    // ---- Gaps and segments ------------------------------------------------------------------------------------------

    [Fact]
    public void SplitSegments_breaks_the_line_at_null_values()
    {
        var points = Series((0, 1), (10, 2), (20, null), (30, 3), (40, 4));

        var segments = HistoryChartMath.SplitSegments(points, TimeSpan.FromSeconds(30));

        Assert.Equal([(0, 2), (3, 2)], segments);
    }

    [Fact]
    public void SplitSegments_breaks_the_line_at_a_gap_larger_than_the_threshold()
    {
        // 20 minutes without data between the third and fourth point.
        var points = Series((0, 1), (10, 1), (20, 1), (1220, 1), (1230, 1));

        var segments = HistoryChartMath.SplitSegments(points, HistoryChartMath.ComputeGapThreshold(points));

        Assert.Equal([(0, 3), (3, 2)], segments);
    }

    [Fact]
    public void SplitSegments_keeps_a_lone_point_and_drops_all_null_series()
    {
        Assert.Equal([(1, 1)], HistoryChartMath.SplitSegments(Series((0, null), (10, 5), (20, null)), TimeSpan.FromSeconds(30)));
        Assert.Empty(HistoryChartMath.SplitSegments(Series((0, null), (10, null)), TimeSpan.FromSeconds(30)));
        Assert.Empty(HistoryChartMath.SplitSegments([], TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void SplitSegments_does_not_bridge_across_a_null_even_when_the_spacing_is_small()
    {
        var points = Series((0, 1), (10, null), (20, 1));

        Assert.Equal([(0, 1), (2, 1)], HistoryChartMath.SplitSegments(points, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ComputeGapThreshold_is_three_times_the_median_spacing()
    {
        // 144 s buckets (a 24 hour chart of 600 points) with one long outage that must not inflate the median.
        var points = Series((0, 1), (144, 1), (288, 1), (432, 1), (1728, 1), (1872, 1), (2016, 1));

        Assert.Equal(TimeSpan.FromSeconds(432), HistoryChartMath.ComputeGapThreshold(points));
    }

    [Fact]
    public void ComputeGapThreshold_never_drops_below_the_floor()
    {
        var points = Series((0, 1), (1, 1), (2, 1), (3, 1));

        Assert.Equal(HistoryChartMath.GapFloor, HistoryChartMath.ComputeGapThreshold(points));
        Assert.Equal(HistoryChartMath.GapFloor, HistoryChartMath.ComputeGapThreshold([]));
        Assert.Equal(HistoryChartMath.GapFloor, HistoryChartMath.ComputeGapThreshold(Series((0, 1))));
    }

    [Fact]
    public void ComputeGapThreshold_ignores_null_points()
    {
        var points = Series((0, 1), (60, null), (120, 1), (240, 1));

        // Spacings between non-null points: 120 s and 120 s.
        Assert.Equal(TimeSpan.FromSeconds(360), HistoryChartMath.ComputeGapThreshold(points));
    }

    // ---- Mapping and nearest point ------------------------------------------------------------------------------------

    [Fact]
    public void MapX_and_UnmapX_are_inverse_and_span_the_whole_range()
    {
        var start = T0;
        var end = T0.AddHours(24);

        Assert.Equal(50, HistoryChartMath.MapX(start, start, end, 50, 800));
        Assert.Equal(850, HistoryChartMath.MapX(end, start, end, 50, 800));
        Assert.Equal(450, HistoryChartMath.MapX(T0.AddHours(12), start, end, 50, 800));
        Assert.Equal(T0.AddHours(6), HistoryChartMath.UnmapX(250, start, end, 50, 800));
    }

    [Fact]
    public void MapY_puts_the_maximum_at_the_top()
    {
        Assert.Equal(10, HistoryChartMath.MapY(100, 0, 100, 10, 200));
        Assert.Equal(210, HistoryChartMath.MapY(0, 0, 100, 10, 200));
        Assert.Equal(110, HistoryChartMath.MapY(50, 0, 100, 10, 200));
    }

    [Fact]
    public void MapX_and_MapY_survive_an_empty_span()
    {
        Assert.Equal(7, HistoryChartMath.MapX(T0, T0, T0, 7, 100));
        Assert.Equal(15, HistoryChartMath.MapY(3, 3, 3, 5, 10));
    }

    [Fact]
    public void FindNearest_snaps_to_the_closest_point_in_time()
    {
        var points = Series((0, 1), (10, 2), (20, 3), (100, 4));

        Assert.Equal(0, HistoryChartMath.FindNearest(points, T0.AddSeconds(-50)));
        Assert.Equal(1, HistoryChartMath.FindNearest(points, T0.AddSeconds(12)));
        Assert.Equal(2, HistoryChartMath.FindNearest(points, T0.AddSeconds(16)));
        Assert.Equal(2, HistoryChartMath.FindNearest(points, T0.AddSeconds(50)));
        Assert.Equal(3, HistoryChartMath.FindNearest(points, T0.AddSeconds(70)));
        Assert.Equal(3, HistoryChartMath.FindNearest(points, T0.AddSeconds(500)));
    }

    [Fact]
    public void FindNearest_skips_null_points_and_handles_empty_series()
    {
        var points = Series((0, null), (10, 2), (20, null), (30, null), (40, 5), (50, null));

        Assert.Equal(1, HistoryChartMath.FindNearest(points, T0.AddSeconds(-5)));
        Assert.Equal(1, HistoryChartMath.FindNearest(points, T0.AddSeconds(24)));
        Assert.Equal(4, HistoryChartMath.FindNearest(points, T0.AddSeconds(26)));
        Assert.Equal(4, HistoryChartMath.FindNearest(points, T0.AddSeconds(55)));
        Assert.Equal(-1, HistoryChartMath.FindNearest([], T0));
        Assert.Equal(-1, HistoryChartMath.FindNearest(Series((0, null), (10, null)), T0));
    }

    // ---- Time axis ---------------------------------------------------------------------------------------------------

    [Fact]
    public void TimeTicks_for_a_day_sit_on_round_local_hours()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+5:30", TimeSpan.FromMinutes(330), "test", "test");

        var ticks = HistoryChartMath.TimeTicks(T0, T0.AddHours(24), 8, zone, out var step);

        Assert.Equal(TimeSpan.FromHours(3), step);
        Assert.NotEmpty(ticks);
        Assert.All(ticks, tick =>
        {
            var local = TimeZoneInfo.ConvertTime(tick, zone);
            Assert.Equal(0, local.Minute);
            Assert.Equal(0, local.Hour % 3);
            Assert.InRange(tick, T0, T0.AddHours(24));
        });
    }

    [Fact]
    public void TimeTicks_for_a_week_sit_on_local_midnights()
    {
        var ticks = HistoryChartMath.TimeTicks(T0, T0.AddDays(7), 8, TimeZoneInfo.Utc, out var step);

        Assert.Equal(TimeSpan.FromDays(1), step);
        Assert.Equal(7, ticks.Count);
        Assert.All(ticks, tick => Assert.Equal(TimeSpan.Zero, tick.TimeOfDay));
    }

    [Fact]
    public void TimeTicks_respect_the_tick_budget()
    {
        foreach (var hours in new[] { 1, 6, 24, 24 * 7, 24 * 30 })
        {
            var ticks = HistoryChartMath.TimeTicks(T0, T0.AddHours(hours), 6, TimeZoneInfo.Utc, out _);

            Assert.InRange(ticks.Count, 1, 7);
        }
    }

    [Fact]
    public void TimeTicks_of_an_empty_range_is_empty()
    {
        Assert.Empty(HistoryChartMath.TimeTicks(T0, T0, 6, TimeZoneInfo.Utc, out _));
    }

    [Theory]
    [InlineData(1, 10, "Time")]
    [InlineData(24, 180, "Time")]
    [InlineData(36, 180, "DateTime")]
    [InlineData(168, 720, "DateTime")]
    [InlineData(168, 1440, "Date")]
    [InlineData(720, 7200, "Date")]
    public void ChooseLabelFormat_depends_on_range_length_and_tick_step(int rangeHours, int stepMinutes, string expected)
    {
        Assert.Equal(Enum.Parse<TimeLabelFormat>(expected), HistoryChartMath.ChooseLabelFormat(TimeSpan.FromHours(rangeHours), TimeSpan.FromMinutes(stepMinutes)));
    }

    [Fact]
    public void FormatTimeLabel_uses_local_time_and_the_given_culture()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test", "test");
        var time = new DateTimeOffset(2026, 10, 19, 12, 30, 0, TimeSpan.Zero);
        var culture = CultureInfo.InvariantCulture;

        Assert.Equal("14:30", HistoryChartMath.FormatTimeLabel(time, TimeLabelFormat.Time, culture, zone));
        Assert.Equal("10/19", HistoryChartMath.FormatTimeLabel(time, TimeLabelFormat.Date, culture, zone));
        Assert.Equal("10/19 14:30", HistoryChartMath.FormatTimeLabel(time, TimeLabelFormat.DateTime, culture, zone));
    }

    [Theory]
    [InlineData("M/d/yyyy", "M/d")]
    [InlineData("dd.MM.yyyy", "dd.MM")]
    [InlineData("yyyy-MM-dd", "MM-dd")]
    [InlineData("yyyy/M/d", "M/d")]
    [InlineData("dd/MM/yyyy", "dd/MM")]
    [InlineData("yyyy", "yyyy")]
    public void ShortDateWithoutYear_drops_the_year_and_its_separator(string pattern, string expected)
    {
        Assert.Equal(expected, HistoryChartMath.ShortDateWithoutYear(pattern));
    }
}
