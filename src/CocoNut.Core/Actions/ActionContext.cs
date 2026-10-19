using System.Globalization;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Actions;

/// <summary>
/// What a fired rule's actions know about the moment it fired: the rule, the UPS and its latest reading. Turns that
/// into the texts the actions use. Every value is a string; a value that is unknown (no reading yet, or the UPS does
/// not report it) is the <b>empty string</b>, consistently in message templates and environment variables.
/// </summary>
/// <param name="Rule">The firing rule.</param>
/// <param name="UpsName">The name of the monitored UPS.</param>
/// <param name="Reading">The reading at that moment, or <see langword="null"/> when there is none (e.g. connection lost).</param>
/// <param name="Timestamp">When the rule fired.</param>
public sealed record ActionContext(ActionRule Rule, string UpsName, UpsReading? Reading, DateTimeOffset Timestamp)
{
    /// <summary>The placeholder names, in the order they are documented.</summary>
    public static readonly IReadOnlyList<string> PlaceholderNames =
        ["rule", "ups", "status", "charge", "runtime", "load", "inputVoltage", "outputVoltage", "outputPower", "batteryVoltage", "time"];

    // Placeholder name -> environment variable of RunCommand. "time" has none.
    private static readonly (string Placeholder, string Variable)[] EnvironmentVariables =
    [
        ("rule", "COCONUT_RULE"),
        ("ups", "COCONUT_UPS"),
        ("status", "COCONUT_STATUS"),
        ("charge", "COCONUT_BATTERY_CHARGE"),
        ("runtime", "COCONUT_BATTERY_RUNTIME"),
        ("load", "COCONUT_LOAD"),
        ("inputVoltage", "COCONUT_INPUT_VOLTAGE"),
        ("outputVoltage", "COCONUT_OUTPUT_VOLTAGE"),
        ("outputPower", "COCONUT_OUTPUT_POWER"),
        ("batteryVoltage", "COCONUT_BATTERY_VOLTAGE"),
    ];

    /// <summary>
    /// The values of the message placeholders (<c>{rule}</c>, <c>{ups}</c>, <c>{status}</c> = raw <c>ups.status</c>,
    /// <c>{charge}</c> in percent, <c>{runtime}</c> in seconds, <c>{load}</c> in percent, <c>{inputVoltage}</c>,
    /// <c>{outputVoltage}</c>, <c>{outputPower}</c> in watts, <c>{batteryVoltage}</c>, <c>{time}</c>). Numbers are
    /// formatted with <paramref name="culture"/> (at most two decimals); <c>{time}</c> is always the ISO 8601 local
    /// time with offset.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetPlaceholders(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule"] = Rule.Name,
            ["ups"] = UpsName,
            ["status"] = Reading?.RawStatus ?? string.Empty,
            ["charge"] = Format(Reading?.BatteryCharge, "0.##", culture),
            ["runtime"] = Format(Reading?.BatteryRuntime?.TotalSeconds, "0", culture),
            ["load"] = Format(Reading?.LoadPercent, "0.##", culture),
            ["inputVoltage"] = Format(Reading?.InputVoltage, "0.##", culture),
            ["outputVoltage"] = Format(Reading?.OutputVoltage, "0.##", culture),
            ["outputPower"] = Format(Reading?.OutputPowerWatts, "0.##", culture),
            ["batteryVoltage"] = Format(Reading?.BatteryVoltage, "0.##", culture),
            ["time"] = Timestamp.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>The <c>COCONUT_*</c> environment variables for <see cref="ActionKind.RunCommand"/>, formatted with the invariant culture.</summary>
    public IReadOnlyDictionary<string, string> GetEnvironment()
    {
        var placeholders = GetPlaceholders(CultureInfo.InvariantCulture);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (placeholder, variable) in EnvironmentVariables)
        {
            environment[variable] = placeholders[placeholder];
        }

        return environment;
    }

    private static string Format(double? value, string format, CultureInfo culture) =>
        value is { } number && double.IsFinite(number) ? number.ToString(format, culture) : string.Empty;
}
