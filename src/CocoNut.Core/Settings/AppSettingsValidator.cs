namespace CocoNut.Core.Settings;

/// <summary>
/// One problem found by <see cref="AppSettingsValidator"/>. <see cref="ErrorKey"/> is a resource key the UI
/// resolves through <c>CocoNut.Localization.Strings</c> (not referenced here, since this project has no UI
/// dependency); <see cref="PropertyPath"/> names the offending property using dotted <see cref="AppSettings"/>
/// notation (e.g. <c>"Connection.Port"</c>) so a settings dialog can highlight the right field.
/// </summary>
public readonly record struct SettingsValidationProblem(string PropertyPath, string ErrorKey);

/// <summary>
/// Validates <see cref="AppSettings"/> against the same limits WinNUT's preferences dialog (<c>Pref_Gui</c>)
/// enforced, so a value that round-trips through this validator behaves the same way in both clients.
/// </summary>
public static class AppSettingsValidator
{
    /// <summary>Minimum accepted poll interval, in milliseconds. WinNUT's NumericUpDown allowed as low as 0.1 s.</summary>
    public const int MinPollIntervalMs = 100;

    /// <summary>Maximum accepted poll interval, in milliseconds (WinNUT's NumericUpDown default maximum of 100 s).</summary>
    public const int MaxPollIntervalMs = 100_000;

    /// <summary>Maximum accepted value, in seconds, for stop/extend delays and the runtime floor (WinNUT: 3600).</summary>
    public const int MaxDelaySeconds = 3600;

    /// <summary>Returns every validation problem found in <paramref name="settings"/>; empty when it is valid.</summary>
    public static IReadOnlyList<SettingsValidationProblem> Validate(AppSettings settings)
    {
        var problems = new List<SettingsValidationProblem>();

        ValidateConnection(settings.Connection, problems);
        ValidateCalibration(settings.Calibration, problems);
        ValidatePower(settings.Power, problems);

        return problems;
    }

    private static void ValidateConnection(ConnectionSettings connection, List<SettingsValidationProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            problems.Add(new SettingsValidationProblem("Connection.Host", "Validation_HostRequired"));
        }

        if (connection.Port is < 1 or > 65535)
        {
            problems.Add(new SettingsValidationProblem("Connection.Port", "Validation_PortRange"));
        }

        if (connection.PollIntervalMs < MinPollIntervalMs || connection.PollIntervalMs > MaxPollIntervalMs)
        {
            problems.Add(new SettingsValidationProblem("Connection.PollIntervalMs", "Validation_PollIntervalRange"));
        }
    }

    private static void ValidateCalibration(CalibrationSettings calibration, List<SettingsValidationProblem> problems)
    {
        AddIfNotLess(problems, calibration.InputVoltageMin, calibration.InputVoltageMax,
            "Calibration.InputVoltageMin", "Validation_CalibrationMinLessThanMax");
        AddIfNotLess(problems, calibration.InputFrequencyMin, calibration.InputFrequencyMax,
            "Calibration.InputFrequencyMin", "Validation_CalibrationMinLessThanMax");
        AddIfNotLess(problems, calibration.OutputVoltageMin, calibration.OutputVoltageMax,
            "Calibration.OutputVoltageMin", "Validation_CalibrationMinLessThanMax");
        AddIfNotLess(problems, calibration.LoadMin, calibration.LoadMax,
            "Calibration.LoadMin", "Validation_CalibrationMinLessThanMax");
        AddIfNotLess(problems, calibration.BatteryVoltageMin, calibration.BatteryVoltageMax,
            "Calibration.BatteryVoltageMin", "Validation_CalibrationMinLessThanMax");
    }

    private static void AddIfNotLess(
        List<SettingsValidationProblem> problems, int min, int max, string propertyPath, string errorKey)
    {
        if (min >= max)
        {
            problems.Add(new SettingsValidationProblem(propertyPath, errorKey));
        }
    }

    private static void ValidatePower(PowerSettings power, List<SettingsValidationProblem> problems)
    {
        if (power.BatteryChargeFloor is < 0 or > 100)
        {
            problems.Add(new SettingsValidationProblem("Power.BatteryChargeFloor", "Validation_ChargeFloorRange"));
        }

        if (power.RuntimeFloorSeconds < 0 || power.RuntimeFloorSeconds > MaxDelaySeconds)
        {
            problems.Add(new SettingsValidationProblem("Power.RuntimeFloorSeconds", "Validation_RuntimeFloorRange"));
        }

        // WinNUT rejected 0 here: a stop/extend delay of zero cannot be used as a timer interval.
        if (power.StopDelaySeconds is < 1 or > MaxDelaySeconds)
        {
            problems.Add(new SettingsValidationProblem("Power.StopDelaySeconds", "Validation_DelayRange"));
        }

        if (power.ExtendDelaySeconds is < 1 or > MaxDelaySeconds)
        {
            problems.Add(new SettingsValidationProblem("Power.ExtendDelaySeconds", "Validation_DelayRange"));
        }
    }
}
