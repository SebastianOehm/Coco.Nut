using System.Globalization;

namespace CocoNut.Core.History;

/// <summary>
/// Writes history as CSV (RFC 4180: comma separated, CRLF line ends, fields with commas, quotes or line breaks
/// quoted). Always uses the invariant culture and ISO-8601 UTC timestamps so the files are machine readable
/// regardless of the user's locale; the column headers are fixed English identifiers, not UI text.
/// </summary>
public static class HistoryCsvExporter
{
    private const string LineEnd = "\r\n";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>Writes a header row and one row per sample. Runtime is in seconds.</summary>
    public static async Task WriteSamplesAsync(
        TextWriter writer, IEnumerable<HistorySample> samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(samples);

        await writer.WriteAsync(
            "timestamp_utc,status,battery_charge_percent,battery_voltage_v,battery_runtime_s,input_voltage_v," +
            "input_frequency_hz,output_voltage_v,load_percent,output_power_w,aggregated" + LineEnd).ConfigureAwait(false);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(string.Join(
                ',',
                FormatTimestamp(sample.Timestamp),
                Quote(sample.Status),
                Format(sample.BatteryCharge),
                Format(sample.BatteryVoltage),
                Format(sample.BatteryRuntimeSeconds),
                Format(sample.InputVoltage),
                Format(sample.InputFrequency),
                Format(sample.OutputVoltage),
                Format(sample.LoadPercent),
                Format(sample.OutputPowerWatts),
                sample.IsAggregated ? "1" : "0") + LineEnd).ConfigureAwait(false);
        }
    }

    /// <summary>Writes a header row and one row per event (kind as its English enum name).</summary>
    public static async Task WriteEventsAsync(
        TextWriter writer, IEnumerable<HistoryEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(events);

        await writer.WriteAsync("timestamp_utc,host,port,ups,kind,detail" + LineEnd).ConfigureAwait(false);

        foreach (var historyEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteAsync(string.Join(
                ',',
                FormatTimestamp(historyEvent.Timestamp),
                Quote(historyEvent.Ups.Host),
                historyEvent.Ups.Port.ToString(CultureInfo.InvariantCulture),
                Quote(historyEvent.Ups.UpsName),
                historyEvent.Kind.ToString(),
                Quote(historyEvent.Detail)) + LineEnd).ConfigureAwait(false);
        }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    private static string Format(double? value) =>
        value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : string.Empty;

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
