using Avalonia.Collections;
using Avalonia.Media;

namespace CocoNut.App.Controls;

/// <summary>
/// Finds the <see cref="GaugeRange"/> band that a value falls into, so the digital display can show the same
/// red/yellow/green status as the dial's scale (see <see cref="GaugePresets"/>).
/// </summary>
/// <remarks>
/// The ranges are expected in ascending order without overlaps, as <see cref="GaugePresets"/> builds them. A value
/// equal to a boundary belongs to the upper band (a band spans <c>Start &lt;= value &lt; End</c>), except the very
/// last <see cref="GaugeRange.End"/>, which belongs to the last band. A value outside the calibrated scale takes
/// the nearest end band (below the first <see cref="GaugeRange.Start"/> the first band, above the last
/// <see cref="GaugeRange.End"/> the last band): being off the scale is the most alarming case, so it must not read
/// as "no status".
/// </remarks>
public static class GaugeRangeLookup
{
    /// <summary>The band that contains <paramref name="value"/>, or <see langword="null"/> when there is no value, no range, or it lies in a gap between ranges.</summary>
    public static GaugeRange? Find(IReadOnlyList<GaugeRange>? ranges, double? value)
    {
        if (ranges is null || ranges.Count == 0 || value is not { } v || double.IsNaN(v))
        {
            return null;
        }

        if (v < ranges[0].Start)
        {
            return ranges[0];
        }

        foreach (GaugeRange range in ranges)
        {
            if (v >= range.Start && v < range.End)
            {
                return range;
            }
        }

        GaugeRange last = ranges[^1];
        return v >= last.End ? last : null;
    }

    /// <summary>The brush of the band returned by <see cref="Find"/>, or <see langword="null"/> when there is none.</summary>
    public static IBrush? FindBrush(AvaloniaList<GaugeRange>? ranges, double? value) => Find(ranges, value)?.Brush;
}
