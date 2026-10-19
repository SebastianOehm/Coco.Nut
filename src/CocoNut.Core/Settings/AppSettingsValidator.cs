using CocoNut.Core.Actions;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;

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

    /// <summary>Accepted range of <see cref="HistorySettings.RetentionDays"/>.</summary>
    public const int MinHistoryRetentionDays = 1;

    /// <inheritdoc cref="MinHistoryRetentionDays"/>
    public const int MaxHistoryRetentionDays = 3650;

    /// <summary>Accepted range of <see cref="HistorySettings.SampleIntervalSeconds"/>.</summary>
    public const int MinHistorySampleIntervalSeconds = 1;

    /// <inheritdoc cref="MinHistorySampleIntervalSeconds"/>
    public const int MaxHistorySampleIntervalSeconds = 3600;

    /// <summary>Returns every validation problem found in <paramref name="settings"/>; empty when it is valid.</summary>
    public static IReadOnlyList<SettingsValidationProblem> Validate(AppSettings settings)
    {
        var problems = new List<SettingsValidationProblem>();

        ValidateConnection(settings.Connection, problems);
        ValidateCalibration(settings.Calibration, problems);
        ValidatePower(settings.Power, problems);
        ValidateHistory(settings.History, problems);
        ValidateActions(settings.Actions, problems);

        return problems;
    }

    /// <summary>Accepted range of a rule's <see cref="ActionRule.DelaySeconds"/> and <see cref="RuleTrigger.DurationSeconds"/> (24 hours).</summary>
    public const int MaxRuleSeconds = 86_400;

    /// <summary>Accepted range of a command action's <see cref="RuleAction.TimeoutSeconds"/> is 1 to this many seconds.</summary>
    public const int MaxCommandTimeoutSeconds = 3600;

    private static void ValidateActions(ActionsSettings? actions, List<SettingsValidationProblem> problems)
    {
        var ids = new HashSet<Guid>();
        var rules = actions?.Rules ?? [];
        for (var i = 0; i < rules.Count; i++)
        {
            var path = $"Actions.Rules[{i}]";
            var rule = rules[i];
            if (rule is null)
            {
                problems.Add(new SettingsValidationProblem(path, "Validation_RuleNameRequired"));
                continue;
            }

            if (rule.Id == Guid.Empty || DefaultRules.IsBuiltIn(rule.Id) || !ids.Add(rule.Id))
            {
                problems.Add(new SettingsValidationProblem($"{path}.Id", "Validation_RuleIdInvalid"));
            }

            if (rule.ShutdownReason != ShutdownReason.CustomRule)
            {
                problems.Add(new SettingsValidationProblem($"{path}.ShutdownReason", "Validation_RuleReasonInvalid"));
            }

            problems.AddRange(ValidateRule(rule, path));
        }
    }

    /// <summary>
    /// Validates what concerns one rule on its own: name, trigger, delays and actions. Not covered here because they
    /// need the whole list or are normalised elsewhere: unique/non-reserved ids and the stop reason (see
    /// <see cref="Validate"/>). <paramref name="path"/> prefixes the property paths of the returned problems.
    /// </summary>
    public static IReadOnlyList<SettingsValidationProblem> ValidateRule(ActionRule rule, string path)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var problems = new List<SettingsValidationProblem>();
        var prefix = path.Length == 0 ? string.Empty : $"{path}.";

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            problems.Add(new SettingsValidationProblem($"{prefix}Name", "Validation_RuleNameRequired"));
        }

        if (rule.DelaySeconds is < 0 or > MaxRuleSeconds)
        {
            problems.Add(new SettingsValidationProblem($"{prefix}DelaySeconds", "Validation_RuleDelayRange"));
        }

        ValidateTrigger(rule.Trigger, $"{prefix}Trigger", problems);

        if (rule.Actions is not { Count: > 0 } || rule.Actions.Any(a => a is null))
        {
            problems.Add(new SettingsValidationProblem($"{prefix}Actions", "Validation_RuleActionsRequired"));
        }
        else
        {
            for (var i = 0; i < rule.Actions.Count; i++)
            {
                ValidateAction(rule.Actions[i], $"{prefix}Actions[{i}]", problems);
            }
        }

        return problems;
    }

    private static void ValidateTrigger(RuleTrigger? trigger, string path, List<SettingsValidationProblem> problems)
    {
        if (trigger is null)
        {
            problems.Add(new SettingsValidationProblem(path, "Validation_RuleFlagsRequired"));
            return;
        }

        switch (trigger.Kind)
        {
            case TriggerKind.FlagsActive or TriggerKind.FlagsBecameActive or TriggerKind.FlagsBecameInactive:
                if (trigger.Flags == UpsStatus.None)
                {
                    problems.Add(new SettingsValidationProblem($"{path}.Flags", "Validation_RuleFlagsRequired"));
                }

                break;

            case TriggerKind.ValueAtOrBelow or TriggerKind.ValueAtOrAbove:
                if (!double.IsFinite(trigger.Threshold))
                {
                    problems.Add(new SettingsValidationProblem($"{path}.Threshold", "Validation_RuleThresholdInvalid"));
                }

                break;
        }

        if (trigger.DurationSeconds is < 0 or > MaxRuleSeconds)
        {
            problems.Add(new SettingsValidationProblem($"{path}.DurationSeconds", "Validation_RuleDurationRange"));
        }
    }

    private static void ValidateAction(RuleAction action, string path, List<SettingsValidationProblem> problems)
    {
        switch (action.Kind)
        {
            case ActionKind.Notify or ActionKind.Log:
                if (string.IsNullOrWhiteSpace(action.Message))
                {
                    problems.Add(new SettingsValidationProblem($"{path}.Message", "Validation_RuleMessageRequired"));
                }

                break;

            case ActionKind.RunCommand:
                if (string.IsNullOrWhiteSpace(action.Command))
                {
                    problems.Add(new SettingsValidationProblem($"{path}.Command", "Validation_RuleCommandRequired"));
                }

                if (action.TimeoutSeconds is < 1 or > MaxCommandTimeoutSeconds)
                {
                    problems.Add(new SettingsValidationProblem($"{path}.TimeoutSeconds", "Validation_RuleTimeoutRange"));
                }

                break;

            case ActionKind.Webhook:
                if (!Uri.TryCreate(action.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                {
                    problems.Add(new SettingsValidationProblem($"{path}.Url", "Validation_RuleUrlInvalid"));
                }

                break;
        }
    }

    private static void ValidateConnection(ConnectionSettings connection, List<SettingsValidationProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(connection.Host))
        {
            problems.Add(new SettingsValidationProblem("Connection.Host", "Validation_HostRequired"));
        }
        else if (!IsValidHost(connection.Host))
        {
            problems.Add(new SettingsValidationProblem("Connection.Host", "Validation_HostInvalid"));
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

    /// <summary>
    /// True when <paramref name="host"/> (trimmed) is an IPv4 address, an IPv6 address (with or without brackets) or a
    /// DNS host name. A protocol, port, path or whitespace makes it invalid.
    /// </summary>
    private static bool IsValidHost(string host)
    {
        var type = Uri.CheckHostName(host.Trim());
        return type is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
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

    private static void ValidateHistory(HistorySettings history, List<SettingsValidationProblem> problems)
    {
        if (history.RetentionDays is < MinHistoryRetentionDays or > MaxHistoryRetentionDays)
        {
            problems.Add(new SettingsValidationProblem("History.RetentionDays", "Validation_HistoryRetentionRange"));
        }

        if (history.SampleIntervalSeconds is < MinHistorySampleIntervalSeconds or > MaxHistorySampleIntervalSeconds)
        {
            problems.Add(new SettingsValidationProblem("History.SampleIntervalSeconds", "Validation_HistorySampleIntervalRange"));
        }
    }
}
