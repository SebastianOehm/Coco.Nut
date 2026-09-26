namespace CocoNut.Core.Ups;

/// <summary>
/// One poll result of a UPS. Values are <see langword="null"/> when the UPS does not report them and they could not be
/// derived. All numbers are parsed with the invariant culture.
/// </summary>
public sealed record UpsReading
{
    public required DateTimeOffset Timestamp { get; init; }
    public required UpsStatus Status { get; init; }
    /// <summary>Raw <c>ups.status</c> value as reported by the server.</summary>
    public string RawStatus { get; init; } = string.Empty;

    /// <summary>Battery charge in percent (0-100).</summary>
    public double? BatteryCharge { get; init; }
    /// <summary><see langword="true"/> when <see cref="BatteryCharge"/> was derived from the battery voltage.</summary>
    public bool BatteryChargeEstimated { get; init; }
    public double? BatteryVoltage { get; init; }
    public TimeSpan? BatteryRuntime { get; init; }
    /// <summary><see langword="true"/> when <see cref="BatteryRuntime"/> was calculated instead of reported.</summary>
    public bool BatteryRuntimeEstimated { get; init; }

    public double? InputVoltage { get; init; }
    public double? InputFrequency { get; init; }
    public double? OutputVoltage { get; init; }
    public double? OutputCurrent { get; init; }
    /// <summary>Load in percent of the UPS capacity.</summary>
    public double? LoadPercent { get; init; }
    /// <summary>Output power in watts, see <see cref="PowerMethod"/>.</summary>
    public double? OutputPowerWatts { get; init; }
}
