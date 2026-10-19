using System.Collections.ObjectModel;
using System.Globalization;
using CocoNut.Core.Actions;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>The localized option lists shared by every rule (and action) editor of the Settings window.</summary>
public sealed class RuleOptions
{
    /// <summary>The message a new notify or log action starts with.</summary>
    public const string DefaultMessage = "{ups}: {status}";

    /// <summary>The <c>ups.status</c> flags a rule can test, in display order.</summary>
    public static readonly IReadOnlyList<UpsStatus> SelectableFlags =
    [
        UpsStatus.OL, UpsStatus.OB, UpsStatus.LB, UpsStatus.HB, UpsStatus.RB, UpsStatus.CHRG, UpsStatus.DISCHRG,
        UpsStatus.BYPASS, UpsStatus.CAL, UpsStatus.OFF, UpsStatus.OVER, UpsStatus.TRIM, UpsStatus.BOOST, UpsStatus.FSD,
        UpsStatus.ALARM, UpsStatus.ECO, UpsStatus.TEST, UpsStatus.COMMLOST,
    ];

    /// <summary>Creates the lists; the stop and log options come from the Settings view model, which already filters
    /// the stop actions by what the platform supports.</summary>
    public RuleOptions(IReadOnlyList<StopActionOption> stopActions, IReadOnlyList<LogLevelOption> logLevels)
    {
        StopActions = stopActions ?? throw new ArgumentNullException(nameof(stopActions));
        LogLevels = logLevels ?? throw new ArgumentNullException(nameof(logLevels));

        TriggerKinds =
        [
            new TriggerKindOption(TriggerKind.FlagsActive, Strings.Prefs_Rules_Trigger_FlagsActive),
            new TriggerKindOption(TriggerKind.FlagsBecameActive, Strings.Prefs_Rules_Trigger_FlagsBecameActive),
            new TriggerKindOption(TriggerKind.FlagsBecameInactive, Strings.Prefs_Rules_Trigger_FlagsBecameInactive),
            new TriggerKindOption(TriggerKind.ValueAtOrBelow, Strings.Prefs_Rules_Trigger_ValueAtOrBelow),
            new TriggerKindOption(TriggerKind.ValueAtOrAbove, Strings.Prefs_Rules_Trigger_ValueAtOrAbove),
            new TriggerKindOption(TriggerKind.OnBatteryFor, Strings.Prefs_Rules_Trigger_OnBatteryFor),
            new TriggerKindOption(TriggerKind.ConnectionLost, Strings.Prefs_Rules_Trigger_ConnectionLost),
        ];

        Metrics =
        [
            new RuleMetricOption(RuleMetric.BatteryCharge, Strings.Prefs_Rules_Metric_BatteryCharge),
            new RuleMetricOption(RuleMetric.BatteryRuntimeSeconds, Strings.Prefs_Rules_Metric_BatteryRuntimeSeconds),
            new RuleMetricOption(RuleMetric.BatteryVoltage, Strings.Prefs_Rules_Metric_BatteryVoltage),
            new RuleMetricOption(RuleMetric.InputVoltage, Strings.Prefs_Rules_Metric_InputVoltage),
            new RuleMetricOption(RuleMetric.OutputVoltage, Strings.Prefs_Rules_Metric_OutputVoltage),
            new RuleMetricOption(RuleMetric.LoadPercent, Strings.Prefs_Rules_Metric_LoadPercent),
            new RuleMetricOption(RuleMetric.OutputPowerWatts, Strings.Prefs_Rules_Metric_OutputPowerWatts),
        ];

        ActionKinds =
        [
            new ActionKindOption(ActionKind.StopComputer, Strings.Prefs_Rules_Action_StopComputer),
            new ActionKindOption(ActionKind.Notify, Strings.Prefs_Rules_Action_Notify),
            new ActionKindOption(ActionKind.Log, Strings.Prefs_Rules_Action_Log),
            new ActionKindOption(ActionKind.RunCommand, Strings.Prefs_Rules_Action_RunCommand),
            new ActionKindOption(ActionKind.Webhook, Strings.Prefs_Rules_Action_Webhook),
        ];

        PlaceholderHint = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Prefs_Rules_Message_Hint,
            string.Join(", ", ActionContext.PlaceholderNames.Select(name => "{" + name + "}")));
    }

    /// <summary>Trigger kinds.</summary>
    public IReadOnlyList<TriggerKindOption> TriggerKinds { get; }

    /// <summary>Values a threshold trigger can compare.</summary>
    public IReadOnlyList<RuleMetricOption> Metrics { get; }

    /// <summary>Action kinds.</summary>
    public IReadOnlyList<ActionKindOption> ActionKinds { get; }

    /// <summary>Stop actions this platform supports.</summary>
    public IReadOnlyList<StopActionOption> StopActions { get; }

    /// <summary>Log levels.</summary>
    public IReadOnlyList<LogLevelOption> LogLevels { get; }

    /// <summary>The "Placeholders: {rule}, {ups}, ..." hint shown below a message field.</summary>
    public string PlaceholderHint { get; }
}

/// <summary>One status flag checkbox of a flag trigger.</summary>
public sealed partial class FlagOptionViewModel : ObservableObject
{
    private readonly Action<FlagOptionViewModel> _changed;

    /// <summary>Creates the checkbox for <paramref name="flag"/>.</summary>
    public FlagOptionViewModel(UpsStatus flag, bool isChecked, Action<FlagOptionViewModel> changed)
    {
        Flag = flag;
        Name = flag.ToString();
        _isChecked = isChecked;
        _changed = changed;
    }

    /// <summary>The flag.</summary>
    public UpsStatus Flag { get; }

    /// <summary>The flag as NUT writes it (OL, OB, ...); a protocol term, not translated.</summary>
    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _changed(this);
}

/// <summary>
/// Edits one <see cref="ActionRule"/> of the working settings in place (Settings &gt; Rules): the list row and the
/// detail editor are the same object. Every edit writes through to the rule and then calls the owner's change
/// callback, which re-validates the settings.
/// </summary>
public sealed partial class RuleViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    /// <summary>Wraps <paramref name="rule"/>; <paramref name="changed"/> is called after every edit.</summary>
    public RuleViewModel(ActionRule rule, RuleOptions options, Action changed)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));

        rule.Trigger ??= new RuleTrigger();
        rule.Actions ??= [];

        _loading = true;
        _name = rule.Name ?? string.Empty;
        _enabled = rule.Enabled;
        _selectedTriggerKind = options.TriggerKinds.First(o => o.Value == rule.Trigger.Kind);
        _selectedMetric = options.Metrics.FirstOrDefault(o => o.Value == rule.Trigger.Metric) ?? options.Metrics[0];
        _threshold = (decimal)Math.Clamp(double.IsFinite(rule.Trigger.Threshold) ? rule.Trigger.Threshold : 0, -1_000_000, 1_000_000);
        _onBatteryOnly = rule.Trigger.OnBatteryOnly;
        _durationSeconds = rule.Trigger.DurationSeconds;
        _delaySeconds = rule.DelaySeconds;

        Flags = [.. RuleOptions.SelectableFlags.Select(flag => new FlagOptionViewModel(flag, rule.Trigger.Flags.HasFlag(flag), OnFlagChanged))];
        Actions = [.. rule.Actions.Where(a => a is not null).Select(CreateActionViewModel)];
        _loading = false;
    }

    /// <summary>The edited rule (an element of the working settings' rule list).</summary>
    public ActionRule Rule { get; }

    /// <summary>The localized option lists.</summary>
    public RuleOptions Options { get; }

    /// <summary>One checkbox per selectable status flag.</summary>
    public IReadOnlyList<FlagOptionViewModel> Flags { get; }

    /// <summary>The rule's actions, in execution order.</summary>
    public ObservableCollection<RuleActionViewModel> Actions { get; }

    /// <summary>Upper bound of the delay/duration fields.</summary>
#pragma warning disable CA1822 // Bound from XAML, which cannot bind to static properties.
    public decimal MaxSeconds => AppSettingsValidator.MaxRuleSeconds;
#pragma warning restore CA1822

    // ---- Fields ----------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _name;

    partial void OnNameChanged(string value) => Edit(() => Rule.Name = value);

    [ObservableProperty]
    private bool _enabled;

    partial void OnEnabledChanged(bool value) => Edit(() => Rule.Enabled = value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlagsTrigger))]
    [NotifyPropertyChangedFor(nameof(IsValueTrigger))]
    [NotifyPropertyChangedFor(nameof(IsDurationTrigger))]
    [NotifyPropertyChangedFor(nameof(IsLevelTrigger))]
    private TriggerKindOption _selectedTriggerKind;

    partial void OnSelectedTriggerKindChanged(TriggerKindOption value)
    {
        Edit(() =>
        {
            Rule.Trigger.Kind = value.Value;
            if (value.Value is TriggerKind.OnBatteryFor or TriggerKind.ConnectionLost && DurationSeconds == 0)
            {
                DurationSeconds = 60;
            }
        });
    }

    [ObservableProperty]
    private RuleMetricOption _selectedMetric;

    partial void OnSelectedMetricChanged(RuleMetricOption value) => Edit(() => Rule.Trigger.Metric = value.Value);

    [ObservableProperty]
    private decimal _threshold;

    partial void OnThresholdChanged(decimal value) => Edit(() => Rule.Trigger.Threshold = (double)value);

    [ObservableProperty]
    private bool _onBatteryOnly;

    partial void OnOnBatteryOnlyChanged(bool value) => Edit(() => Rule.Trigger.OnBatteryOnly = value);

    [ObservableProperty]
    private decimal _durationSeconds;

    partial void OnDurationSecondsChanged(decimal value) => Edit(() => Rule.Trigger.DurationSeconds = ToSeconds(value));

    [ObservableProperty]
    private decimal _delaySeconds;

    partial void OnDelaySecondsChanged(decimal value) => Edit(() => Rule.DelaySeconds = ToSeconds(value));

    // ---- Which fields apply to the selected trigger kind -------------------------------------------------------------

    /// <summary>The trigger tests status flags.</summary>
    public bool IsFlagsTrigger => SelectedTriggerKind.Value is TriggerKind.FlagsActive or TriggerKind.FlagsBecameActive or TriggerKind.FlagsBecameInactive;

    /// <summary>The trigger compares a value with a threshold.</summary>
    public bool IsValueTrigger => SelectedTriggerKind.Value is TriggerKind.ValueAtOrBelow or TriggerKind.ValueAtOrAbove;

    /// <summary>The trigger has a duration (on battery for, connection lost for).</summary>
    public bool IsDurationTrigger => SelectedTriggerKind.Value is TriggerKind.OnBatteryFor or TriggerKind.ConnectionLost;

    /// <summary>The trigger is a level (a condition that holds), so the delay applies; edge triggers fire at the moment of the change.</summary>
    public bool IsLevelTrigger => SelectedTriggerKind.Value is not (TriggerKind.FlagsBecameActive or TriggerKind.FlagsBecameInactive);

    // ---- Derived -----------------------------------------------------------------------------------------------------

    /// <summary>Whether one of the actions stops this computer (shows the warning).</summary>
    public bool StopsComputer => Rule.StopsComputer();

    /// <summary>One line, localized: "trigger → actions".</summary>
    public string Summary => string.Format(CultureInfo.CurrentCulture, Strings.Prefs_Rules_Summary, TriggerSummary(), ActionsSummary());

    /// <summary>The validation problems of this rule, localized; set by the owner on every validation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblems))]
    private IReadOnlyList<string> _problems = [];

    /// <summary>Whether this rule currently fails validation.</summary>
    public bool HasProblems => Problems.Count > 0;

    // ---- Actions -------------------------------------------------------------------------------------------------------

    /// <summary>Appends a new action (a notification) to the rule.</summary>
    [RelayCommand]
    private void AddAction()
    {
        var action = RuleAction.Notify(RuleOptions.DefaultMessage);
        Rule.Actions.Add(action);
        Actions.Add(CreateActionViewModel(action));
        Edit(() => { });
    }

    private RuleActionViewModel CreateActionViewModel(RuleAction action) => new(action, Options, OnActionChanged, Remove);

    private void Remove(RuleActionViewModel action)
    {
        Rule.Actions.Remove(action.Model);
        Actions.Remove(action);
        Edit(() => { });
    }

    private void OnActionChanged()
    {
        if (!_loading)
        {
            Touched();
        }
    }

    private void OnFlagChanged(FlagOptionViewModel _) =>
        Edit(() => Rule.Trigger.Flags = Flags.Where(f => f.IsChecked).Aggregate(UpsStatus.None, (all, f) => all | f.Flag));

    private void Edit(Action apply)
    {
        if (_loading)
        {
            return;
        }

        apply();
        Touched();
    }

    private void Touched()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(StopsComputer));
        _changed();
    }

    private static int ToSeconds(decimal value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    private string TriggerSummary()
    {
        var trigger = Rule.Trigger;
        var culture = CultureInfo.CurrentCulture;
        switch (trigger.Kind)
        {
            case TriggerKind.FlagsActive or TriggerKind.FlagsBecameActive or TriggerKind.FlagsBecameInactive:
                var names = Flags.Where(f => f.IsChecked).Select(f => f.Name).ToArray();
                var joined = names.Length == 0 ? Strings.Prefs_Rules_Summary_NoFlags : string.Join(" + ", names);
                var format = trigger.Kind switch
                {
                    TriggerKind.FlagsActive => Strings.Prefs_Rules_Summary_FlagsActive,
                    TriggerKind.FlagsBecameActive => Strings.Prefs_Rules_Summary_FlagsBecameActive,
                    _ => Strings.Prefs_Rules_Summary_FlagsBecameInactive,
                };
                return string.Format(culture, format, joined);

            case TriggerKind.ValueAtOrBelow or TriggerKind.ValueAtOrAbove:
                var metric = Options.Metrics.FirstOrDefault(o => o.Value == trigger.Metric)?.Text ?? trigger.Metric.ToString();
                var value = double.IsFinite(trigger.Threshold) ? trigger.Threshold.ToString("0.##", culture) : "?";
                var text = string.Format(
                    culture,
                    trigger.Kind == TriggerKind.ValueAtOrBelow ? Strings.Prefs_Rules_Summary_ValueAtOrBelow : Strings.Prefs_Rules_Summary_ValueAtOrAbove,
                    metric,
                    value);
                return trigger.OnBatteryOnly ? string.Format(culture, Strings.Prefs_Rules_Summary_OnBatteryOnly, text) : text;

            case TriggerKind.OnBatteryFor:
                return string.Format(culture, Strings.Prefs_Rules_Summary_OnBatteryFor, trigger.DurationSeconds);

            default:
                return string.Format(culture, Strings.Prefs_Rules_Summary_ConnectionLost, trigger.DurationSeconds);
        }
    }

    private string ActionsSummary()
    {
        if (Rule.Actions.Count == 0)
        {
            return Strings.Prefs_Rules_Summary_NoActions;
        }

        return string.Join(
            ", ",
            Rule.Actions.Where(a => a is not null).Select(a => a.Kind == ActionKind.StopComputer
                ? Options.StopActions.FirstOrDefault(o => o.Value == a.StopAction)?.Text ?? Strings.Prefs_Rules_Action_StopComputer
                : Options.ActionKinds.First(o => o.Value == a.Kind).Text));
    }
}

/// <summary>Edits one <see cref="RuleAction"/> of a rule; shows only the fields of its kind.</summary>
public sealed partial class RuleActionViewModel : ObservableObject
{
    private readonly Action _changed;
    private bool _loading;

    /// <summary>Wraps <paramref name="action"/>.</summary>
    public RuleActionViewModel(RuleAction action, RuleOptions options, Action changed, Action<RuleActionViewModel> remove)
    {
        Model = action ?? throw new ArgumentNullException(nameof(action));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        ArgumentNullException.ThrowIfNull(remove);
        RemoveCommand = new RelayCommand(() => remove(this));

        action.Arguments ??= [];

        _loading = true;
        _selectedKind = options.ActionKinds.First(o => o.Value == action.Kind);
        _selectedStopAction = options.StopActions.FirstOrDefault(o => o.Value == action.StopAction) ?? options.StopActions[0];
        _message = action.Message ?? string.Empty;
        _selectedLogLevel = options.LogLevels.FirstOrDefault(o => o.Value == action.LogLevel) ?? options.LogLevels[0];
        _command = action.Command ?? string.Empty;
        _argumentsText = string.Join('\n', action.Arguments);
        _timeoutSeconds = action.TimeoutSeconds;
        _url = action.Url ?? string.Empty;
        _loading = false;
    }

    /// <summary>The edited action.</summary>
    public RuleAction Model { get; }

    /// <summary>The localized option lists.</summary>
    public RuleOptions Options { get; }

    /// <summary>Removes this action from its rule.</summary>
    public IRelayCommand RemoveCommand { get; }

    /// <summary>Upper bound of the timeout field.</summary>
#pragma warning disable CA1822 // Bound from XAML, which cannot bind to static properties.
    public decimal MaxTimeoutSeconds => AppSettingsValidator.MaxCommandTimeoutSeconds;
#pragma warning restore CA1822

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStop))]
    [NotifyPropertyChangedFor(nameof(IsMessage))]
    [NotifyPropertyChangedFor(nameof(IsLog))]
    [NotifyPropertyChangedFor(nameof(IsCommand))]
    [NotifyPropertyChangedFor(nameof(IsWebhook))]
    private ActionKindOption _selectedKind;

    partial void OnSelectedKindChanged(ActionKindOption value)
    {
        Edit(() =>
        {
            Model.Kind = value.Value;
            if (IsMessage && string.IsNullOrWhiteSpace(Message))
            {
                Message = RuleOptions.DefaultMessage;
            }
        });
    }

    [ObservableProperty]
    private StopActionOption _selectedStopAction;

    partial void OnSelectedStopActionChanged(StopActionOption value) => Edit(() => Model.StopAction = value.Value);

    [ObservableProperty]
    private string _message;

    partial void OnMessageChanged(string value) => Edit(() => Model.Message = value);

    [ObservableProperty]
    private LogLevelOption _selectedLogLevel;

    partial void OnSelectedLogLevelChanged(LogLevelOption value) => Edit(() => Model.LogLevel = value.Value);

    [ObservableProperty]
    private string _command;

    partial void OnCommandChanged(string value) => Edit(() => Model.Command = value);

    /// <summary>The arguments, one per line (blank lines are dropped).</summary>
    [ObservableProperty]
    private string _argumentsText;

    partial void OnArgumentsTextChanged(string value) =>
        Edit(() => Model.Arguments = [.. (value ?? string.Empty).Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)]);

    [ObservableProperty]
    private decimal _timeoutSeconds;

    partial void OnTimeoutSecondsChanged(decimal value) =>
        Edit(() => Model.TimeoutSeconds = (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue));

    [ObservableProperty]
    private string _url;

    partial void OnUrlChanged(string value) => Edit(() => Model.Url = value);

    /// <summary>Stop-computer action.</summary>
    public bool IsStop => SelectedKind.Value == ActionKind.StopComputer;

    /// <summary>Notify or log action (they have a message).</summary>
    public bool IsMessage => SelectedKind.Value is ActionKind.Notify or ActionKind.Log;

    /// <summary>Log action.</summary>
    public bool IsLog => SelectedKind.Value == ActionKind.Log;

    /// <summary>Run-command action.</summary>
    public bool IsCommand => SelectedKind.Value == ActionKind.RunCommand;

    /// <summary>Webhook action.</summary>
    public bool IsWebhook => SelectedKind.Value == ActionKind.Webhook;

    private void Edit(Action apply)
    {
        if (_loading)
        {
            return;
        }

        apply();
        _changed();
    }
}
