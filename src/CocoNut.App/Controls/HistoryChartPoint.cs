namespace CocoNut.App.Controls;

/// <summary>One sample of a <see cref="HistoryChart"/> series; <see cref="Value"/> is <see langword="null"/> when the UPS did not report it.</summary>
public readonly record struct HistoryChartPoint(DateTimeOffset Timestamp, double? Value);
