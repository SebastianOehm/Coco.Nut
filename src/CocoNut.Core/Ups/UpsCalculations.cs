using System.Globalization;

namespace CocoNut.Core.Ups;

/// <summary>
/// Pure calculations ported from WinNUT's <c>UPS_Device.vb</c> (product info / power method selection,
/// <c>Retrieve_UPS_Datas</c> for battery charge and runtime estimation). Every method here is deterministic and
/// side-effect free so it can be unit tested against hand-computed values without a NUT server or a clock.
/// </summary>
public static class UpsCalculations
{
    /// <summary>
    /// Power factor used by WinNUT for the VA-based power estimations (<see cref="PowerMethod.InputNominalVaLoadPercent"/>
    /// and <see cref="PowerMethod.OutputVaCalculation"/>). See <c>UPS_Device.POWER_FACTOR</c>.
    /// </summary>
    public const double PowerFactor = 0.8;

    /// <summary>
    /// Chooses how output power should be calculated, in the same precedence WinNUT used in
    /// <c>UPS_Device.GetUPSProductInfo</c>: a directly reported watt value wins, then load-percent based
    /// estimations from progressively less specific nominal values, then a VA calculation, and finally
    /// <see cref="PowerMethod.Unavailable"/> when none of the required variables are present.
    /// </summary>
    /// <param name="vars">All variables from one <c>LIST VAR</c> response.</param>
    public static PowerMethod SelectPowerMethod(IReadOnlyDictionary<string, string> vars)
    {
        ArgumentNullException.ThrowIfNull(vars);

        if (vars.ContainsKey("output.realpower"))
        {
            return PowerMethod.RealOutputPower;
        }

        if (vars.ContainsKey("ups.realpower"))
        {
            return PowerMethod.RealPower;
        }

        if (vars.ContainsKey("ups.realpower.nominal") && vars.ContainsKey("ups.load"))
        {
            return PowerMethod.RealPowerNominalLoadPercent;
        }

        if (vars.ContainsKey("input.current.nominal") && vars.ContainsKey("input.voltage.nominal") && vars.ContainsKey("ups.load"))
        {
            return PowerMethod.InputNominalVaLoadPercent;
        }

        if (vars.ContainsKey("output.current") && vars.ContainsKey("output.voltage"))
        {
            return PowerMethod.OutputVaCalculation;
        }

        return PowerMethod.Unavailable;
    }

    /// <summary>
    /// Computes output power in watts for <paramref name="method"/>, rounded to one decimal place like
    /// WinNUT's <c>Math.Round(parsedValue, 1)</c> in <c>Retrieve_UPS_Datas</c>.
    /// </summary>
    /// <param name="method">The method selected by <see cref="SelectPowerMethod"/>.</param>
    /// <param name="vars">All variables from one <c>LIST VAR</c> response.</param>
    /// <param name="load"><c>ups.load</c> already parsed by the caller (percent, 0-100), or <see langword="null"/>
    /// when unknown. Not read from <paramref name="vars"/> directly so the caller's single parse is reused.</param>
    /// <returns><see langword="null"/> when a required variable is missing or unparsable.</returns>
    public static double? ComputePower(PowerMethod method, IReadOnlyDictionary<string, string> vars, double? load)
    {
        ArgumentNullException.ThrowIfNull(vars);

        double? raw = method switch
        {
            PowerMethod.RealOutputPower => ParseDoubleOrNull(vars, "output.realpower"),
            PowerMethod.RealPower => ParseDoubleOrNull(vars, "ups.realpower"),
            PowerMethod.RealPowerNominalLoadPercent => ComputeNominalLoad(vars, "ups.realpower.nominal", load, applyPowerFactor: false),
            PowerMethod.InputNominalVaLoadPercent => ComputeInputNominalVa(vars, load),
            PowerMethod.OutputVaCalculation => ComputeOutputVa(vars),
            _ => null,
        };

        return raw is double value ? Math.Round(value, 1) : null;
    }

    /// <summary>
    /// Estimates battery charge (percent) from battery voltage, for UPS units that do not report
    /// <c>battery.charge</c> directly. Ported from <c>Retrieve_UPS_Datas</c>:
    /// <c>nBatt = Floor(V / 12)</c>, <c>charge = Floor((V - 11.6 * nBatt) / (0.02 * nBatt))</c>.
    /// </summary>
    /// <param name="batteryVoltage"><c>battery.voltage</c>.</param>
    /// <returns>
    /// The estimated charge clamped to 0-100 (WinNUT did not clamp; the raw formula can slightly overshoot at the
    /// band edges, so clamping avoids reporting e.g. 104%). <see langword="null"/> when <paramref name="batteryVoltage"/>
    /// is not positive or rounds down to less than one 12V battery cell (<c>nBatt = 0</c>), which would otherwise
    /// divide by zero.
    /// </returns>
    public static double? EstimateBatteryChargeFromVoltage(double batteryVoltage)
    {
        if (batteryVoltage <= 0 || double.IsNaN(batteryVoltage))
        {
            return null;
        }

        var batteryCellCount = Math.Floor(batteryVoltage / 12.0);
        if (batteryCellCount <= 0)
        {
            return null;
        }

        var charge = Math.Floor((batteryVoltage - 11.6 * batteryCellCount) / (0.02 * batteryCellCount));
        return Math.Clamp(charge, 0.0, 100.0);
    }

    /// <summary>
    /// Estimates remaining runtime when the UPS does not report <c>battery.runtime</c>. Ported verbatim from
    /// <c>Retrieve_UPS_Datas</c>:
    /// <code>
    /// PowerDivider = Load in 76..100 ? 0.4 : Load in 51..75 ? 0.3 : 0.5
    /// loadForCalc  = Load == 0 ? 0.1 : Load   ' substitution used only for this formula
    /// BattInstantCurrent = (OutputVoltage * loadForCalc) / (BatteryVoltage * 100)
    /// runtime = Floor(BatteryCapacity * 0.6 * BatteryCharge * (1 - PowerDivider) * 3600 / (BattInstantCurrent * 100))
    /// </code>
    /// </summary>
    /// <param name="outputVoltage"><c>output.voltage</c>.</param>
    /// <param name="load"><c>ups.load</c> percent, as reported (0 is valid and must be passed through unchanged;
    /// the 0 → 0.1 substitution WinNUT applies is purely internal to this formula, see remarks on
    /// <see cref="BuildReading"/>).</param>
    /// <param name="batteryVoltage"><c>battery.voltage</c>.</param>
    /// <param name="batteryCapacity"><c>battery.capacity</c> (Ah), read once at connect time.</param>
    /// <param name="batteryCharge">Battery charge percent, possibly itself estimated by
    /// <see cref="EstimateBatteryChargeFromVoltage"/> (WinNUT computes the charge estimate before the runtime
    /// estimate and reuses it here).</param>
    /// <returns>
    /// The estimated runtime, or <see langword="null"/> when <paramref name="batteryVoltage"/> or
    /// <paramref name="outputVoltage"/> is not positive (WinNUT would divide by zero, producing <c>Infinity</c>,
    /// which the UI then treats as "out of range"; we return <see langword="null"/> directly to avoid an
    /// overflow when converting to <see cref="TimeSpan"/>) or when the result does not fit in a
    /// <see cref="TimeSpan"/>.
    /// </returns>
    public static TimeSpan? EstimateRuntime(double outputVoltage, double load, double batteryVoltage, double batteryCapacity, double batteryCharge)
    {
        if (batteryVoltage <= 0 || double.IsNaN(batteryVoltage))
        {
            return null;
        }

        var powerDivider = load switch
        {
            >= 76 and <= 100 => 0.4,
            >= 51 and <= 75 => 0.3,
            _ => 0.5,
        };

        // WinNUT: ".Load = If(.Load <> 0, .Load, 0.1)" - substitution local to this formula; the reported load
        // (UpsReading.LoadPercent) must stay unchanged, so the caller passes the untouched value in.
        var loadForCalculation = load != 0 ? load : 0.1;

        var battInstantCurrent = (outputVoltage * loadForCalculation) / (batteryVoltage * 100);
        if (battInstantCurrent == 0 || double.IsNaN(battInstantCurrent))
        {
            return null;
        }

        var seconds = Math.Floor(batteryCapacity * 0.6 * batteryCharge * (1 - powerDivider) * 3600 / (battInstantCurrent * 100));
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Builds one poll's <see cref="UpsReading"/> from a <c>LIST VAR</c> response, replicating
    /// <c>UPS_Device.Retrieve_UPS_Datas</c>: parses with the invariant culture, estimates battery charge from
    /// voltage when <c>battery.charge</c> is missing or out of the 0-100 range, estimates runtime when
    /// <c>battery.runtime</c> is missing (using the possibly-estimated charge, same order as WinNUT), and resolves
    /// the frequency fallback chain <c>input.frequency → output.frequency → output.frequency.nominal →</c>
    /// <paramref name="nominalFrequencyFallback"/> (WinNUT only had <c>input.frequency</c> falling back to the
    /// nominal captured at connect time; the extra <c>output.frequency</c> step is an addition for UPS units that
    /// only report frequency on the output side).
    /// </summary>
    /// <param name="vars">All variables from one <c>LIST VAR</c> response.</param>
    /// <param name="method">The power method selected once per connection by <see cref="SelectPowerMethod"/>.</param>
    /// <param name="batteryCapacity"><c>battery.capacity</c>, read once at connect time (<see langword="null"/> when
    /// the UPS does not report it).</param>
    /// <param name="nominalFrequencyFallback">The configured nominal frequency (WinNUT: <c>CAL_FreqInNom</c>), used
    /// only when the UPS reports no frequency variable at all.</param>
    /// <param name="now">The poll timestamp.</param>
    public static UpsReading BuildReading(
        IReadOnlyDictionary<string, string> vars,
        PowerMethod method,
        double? batteryCapacity,
        double nominalFrequencyFallback,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(vars);

        var rawStatus = vars.GetValueOrDefault("ups.status") ?? string.Empty;
        var status = UpsStatusParser.Parse(rawStatus, out _);

        var batteryVoltage = ParseDoubleOrNull(vars, "battery.voltage");
        var outputVoltage = ParseDoubleOrNull(vars, "output.voltage");
        var loadPercent = ParseDoubleOrNull(vars, "ups.load");

        var (batteryCharge, batteryChargeEstimated) = ResolveBatteryCharge(vars, batteryVoltage);
        var (batteryRuntime, batteryRuntimeEstimated) = ResolveBatteryRuntime(
            vars, outputVoltage, loadPercent, batteryVoltage, batteryCapacity, batteryCharge);

        var frequency = ParseDoubleOrNull(vars, "input.frequency")
            ?? ParseDoubleOrNull(vars, "output.frequency")
            ?? ParseDoubleOrNull(vars, "output.frequency.nominal")
            ?? nominalFrequencyFallback;

        return new UpsReading
        {
            Timestamp = now,
            Status = status,
            RawStatus = rawStatus,
            BatteryCharge = batteryCharge,
            BatteryChargeEstimated = batteryChargeEstimated,
            BatteryVoltage = batteryVoltage,
            BatteryRuntime = batteryRuntime,
            BatteryRuntimeEstimated = batteryRuntimeEstimated,
            InputVoltage = ParseDoubleOrNull(vars, "input.voltage"),
            InputFrequency = frequency,
            OutputVoltage = outputVoltage,
            OutputCurrent = ParseDoubleOrNull(vars, "output.current"),
            LoadPercent = loadPercent,
            OutputPowerWatts = ComputePower(method, vars, loadPercent),
        };
    }

    /// <summary>
    /// Builds static product info, with the same manufacturer/model/serial fallbacks WinNUT used in
    /// <c>GetUPSProductInfo</c> (<c>ups.*</c> preferred, <c>device.*</c> as a fallback, <c>"Unknown"</c> when
    /// neither is present).
    /// </summary>
    /// <param name="vars">All variables from one <c>LIST VAR</c> response.</param>
    public static UpsInfo BuildInfo(IReadOnlyDictionary<string, string> vars)
    {
        ArgumentNullException.ThrowIfNull(vars);

        return new UpsInfo(
            FirstNonEmpty(vars, "ups.mfr", "device.mfr"),
            FirstNonEmpty(vars, "ups.model", "device.model"),
            FirstNonEmpty(vars, "ups.serial", "device.serial"),
            FirstNonEmpty(vars, "ups.firmware"));
    }

    private static (double? Charge, bool Estimated) ResolveBatteryCharge(IReadOnlyDictionary<string, string> vars, double? batteryVoltage)
    {
        var rawCharge = ParseDoubleOrNull(vars, "battery.charge");

        // WinNUT: "If .Batt_Charge < 0 OrElse .Batt_Charge > 100" (its GetUPSVar fallback for a missing variable is
        // -1, which also falls into this range check).
        if (rawCharge is double charge && charge is >= 0 and <= 100)
        {
            return (charge, false);
        }

        if (batteryVoltage is double voltage && voltage > 0)
        {
            var estimated = EstimateBatteryChargeFromVoltage(voltage);
            if (estimated is not null)
            {
                return (estimated, true);
            }
        }

        return (null, false);
    }

    private static (TimeSpan? Runtime, bool Estimated) ResolveBatteryRuntime(
        IReadOnlyDictionary<string, string> vars,
        double? outputVoltage,
        double? loadPercent,
        double? batteryVoltage,
        double? batteryCapacity,
        double? batteryCharge)
    {
        var rawRuntime = ParseDoubleOrNull(vars, "battery.runtime");
        if (rawRuntime is double seconds && seconds >= 0)
        {
            return (TimeSpan.FromSeconds(seconds), false);
        }

        // WinNUT only attempts the estimate when every input variable is available.
        if (outputVoltage is double ov && batteryVoltage is double bv && batteryCapacity is double bc && batteryCharge is double charge)
        {
            var estimated = EstimateRuntime(ov, loadPercent ?? 0, bv, bc, charge);
            if (estimated is not null)
            {
                return (estimated, true);
            }
        }

        return (null, false);
    }

    private static double? ComputeNominalLoad(IReadOnlyDictionary<string, string> vars, string nominalKey, double? load, bool applyPowerFactor)
    {
        var nominal = ParseDoubleOrNull(vars, nominalKey);
        if (nominal is null)
        {
            return null;
        }

        var value = nominal.Value * (applyPowerFactor ? PowerFactor : 1.0);
        return value * (load ?? 0) / 100.0;
    }

    private static double? ComputeInputNominalVa(IReadOnlyDictionary<string, string> vars, double? load)
    {
        var current = ParseDoubleOrNull(vars, "input.current.nominal");
        var voltage = ParseDoubleOrNull(vars, "input.voltage.nominal");
        if (current is null || voltage is null)
        {
            return null;
        }

        return current.Value * voltage.Value * PowerFactor * (load ?? 0) / 100.0;
    }

    private static double? ComputeOutputVa(IReadOnlyDictionary<string, string> vars)
    {
        var current = ParseDoubleOrNull(vars, "output.current");
        var voltage = ParseDoubleOrNull(vars, "output.voltage");
        if (current is null || voltage is null)
        {
            return null;
        }

        return current.Value * voltage.Value * PowerFactor;
    }

    private static string FirstNonEmpty(IReadOnlyDictionary<string, string> vars, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (vars.TryGetValue(key, out var value))
            {
                var trimmed = value.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }
        }

        return "Unknown";
    }

    private static double? ParseDoubleOrNull(IReadOnlyDictionary<string, string> vars, string key)
    {
        if (vars.TryGetValue(key, out var raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }
}
