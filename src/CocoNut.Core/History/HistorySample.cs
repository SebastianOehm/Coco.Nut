using CocoNut.Core.Ups;

namespace CocoNut.Core.History;

/// <summary>
/// One data point of the history. Values are <see langword="null"/> when the UPS did not report them.
/// <paramref name="IsAggregated"/> marks a point that is the average of several raw readings (downsampled old data,
/// or a bucket returned for a chart).
/// </summary>
public sealed record HistorySample(
    DateTimeOffset Timestamp,
    string Status,
    double? BatteryCharge = null,
    double? BatteryVoltage = null,
    double? BatteryRuntimeSeconds = null,
    double? InputVoltage = null,
    double? InputFrequency = null,
    double? OutputVoltage = null,
    double? LoadPercent = null,
    double? OutputPowerWatts = null,
    bool IsAggregated = false)
{
    /// <summary>Maps a poll result to a raw (not aggregated) sample.</summary>
    public static HistorySample FromReading(UpsReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return new HistorySample(
            reading.Timestamp,
            reading.RawStatus,
            reading.BatteryCharge,
            reading.BatteryVoltage,
            reading.BatteryRuntime?.TotalSeconds,
            reading.InputVoltage,
            reading.InputFrequency,
            reading.OutputVoltage,
            reading.LoadPercent,
            reading.OutputPowerWatts);
    }
}
