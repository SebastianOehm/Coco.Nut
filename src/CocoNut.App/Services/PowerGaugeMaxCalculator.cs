using CocoNut.Core.Ups;

namespace CocoNut.App.Services;

/// <summary>
/// Derives the power gauge's maximum (there is no calibration setting for it, unlike the other five gauges -
/// see <see cref="CocoNut.App.Controls.GaugePresets.Power"/>): the UPS's nominal power, estimated from one
/// reading's load percentage and output power, rounded up to a "nice" round number.
/// </summary>
public static class PowerGaugeMaxCalculator
{
    /// <summary>Used when a reading is unavailable or does not carry enough information to estimate a nominal power.</summary>
    public const double DefaultMax = 1000d;

    /// <summary>Round numbers a UPS's nominal power is commonly rated at, ascending.</summary>
    private static readonly double[] NiceValues =
    [
        100, 250, 500, 1000, 1500, 2000, 3000, 5000, 7500, 10_000, 15_000, 20_000, 30_000, 50_000, 75_000, 100_000,
    ];

    /// <summary>
    /// Estimates the UPS's nominal power from <paramref name="reading"/> (<c>power / (load / 100)</c>) and rounds
    /// it up to the next <see cref="NiceValues"/> entry, or <see cref="DefaultMax"/> when the reading has no
    /// positive load percentage and output power to estimate from.
    /// </summary>
    public static double DeriveNominal(UpsReading? reading)
    {
        if (reading is null || reading.LoadPercent is not > 0 || reading.OutputPowerWatts is not double power)
        {
            return DefaultMax;
        }

        double load = reading.LoadPercent.Value;
        double nominal = power / (load / 100.0);
        foreach (var nice in NiceValues)
        {
            if (nice >= nominal)
            {
                return nice;
            }
        }

        return Math.Ceiling(nominal / 1000.0) * 1000.0;
    }

    /// <summary>
    /// Combines the nominal power estimated from <paramref name="reading"/> with <paramref name="currentMax"/> so
    /// the gauge's maximum only ever grows during a session, never shrinks back down on a lower reading.
    /// </summary>
    public static double NextMax(double currentMax, UpsReading? reading) => Math.Max(currentMax, DeriveNominal(reading));
}
