using CocoNut.App.Controls;
using CocoNut.Core.History;

namespace CocoNut.App.ViewModels;

/// <summary>One entry of the history window's time range selector.</summary>
public sealed record HistoryRangeOption(TimeSpan Duration, string Text)
{
    /// <inheritdoc />
    public override string ToString() => Text;
}

/// <summary>
/// One metric of the history window: everything a <see cref="HistoryChart"/> needs. <paramref name="Minimum"/> and
/// <paramref name="Maximum"/> fix the y-axis (null scales it to the data).
/// </summary>
public sealed record HistoryChartSeries(
    string Caption,
    string Unit,
    string ValueFormat,
    double? Minimum,
    double? Maximum,
    IReadOnlyList<HistoryChartPoint> Points,
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd)
{
    /// <summary>Whether at least one point carries a value.</summary>
    public bool HasData => Points.Any(point => point.Value is not null);
}

/// <summary>One row of the history window's event list, with its display texts formatted once for the current culture.</summary>
public sealed record HistoryEventRow(DateTimeOffset Timestamp, HistoryEventKind Kind, string TimeText, string KindText, string Detail);
