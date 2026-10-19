using System.Globalization;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Actions;

/// <summary>A rule that fires in one <see cref="RuleEngine.Evaluate"/> call.</summary>
/// <param name="Rule">The firing rule.</param>
/// <param name="Detail">A human-readable explanation, suitable for logging.</param>
/// <param name="IsNewFiring">
/// <see langword="true"/> for an edge occurrence or the rising edge of a level condition; <see langword="false"/> for
/// the repeated report of a stop rule whose condition keeps holding. Actions other than stopping the computer run on
/// new firings only.
/// </param>
public sealed record RuleMatch(ActionRule Rule, string Detail, bool IsNewFiring = true);

/// <summary>
/// Pure rule evaluation: decides which <see cref="ActionRule"/>s fire for a UPS reading / status change / connection
/// state, remembering per rule (keyed by <see cref="ActionRule.Id"/>) since when its level condition has held and
/// whether it already fired. Not thread-safe; callers serialise access.
/// <para>
/// Semantics: edge triggers fire on every occurrence of the edge (they only match when a status change is passed).
/// Level triggers fire once per rising edge, after the condition has held for <see cref="ActionRule.DelaySeconds"/>
/// (plus <see cref="RuleTrigger.DurationSeconds"/> for the duration triggers), and re-arm when the condition becomes
/// false. <b>Rules that stop the computer are the exception</b>: while their condition holds they are reported on
/// <i>every</i> evaluation, not just once. The shutdown coordinator guards against repeats itself and must be able
/// to start a stop again after its re-arm delay if the condition still holds, exactly like the former fixed policy.
/// Disabled rules never fire (and forget their state); rules scoped to a named UPS are ignored for now.
/// </para>
/// <para>
/// Without a reading (<see langword="null"/>, e.g. a timer tick while disconnected) only the connection-lost rules
/// are evaluated; other level rules keep their state untouched.
/// </para>
/// </summary>
public sealed class RuleEngine
{
    private sealed class RuleState
    {
        public DateTimeOffset? Since;
        public bool Fired;
    }

    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<Guid, RuleState> _states = [];

    public RuleEngine(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Evaluates <paramref name="rules"/> in order and returns the ones that fire now, in that order.
    /// </summary>
    /// <param name="reading">The latest reading, or <see langword="null"/> when none is available / for a time-only tick.</param>
    /// <param name="statusChange">The status change this call responds to, or <see langword="null"/>; used by the edge triggers.</param>
    /// <param name="connectionLost">Whether the monitor's connection to the server is currently lost.</param>
    /// <param name="rules">The current rule set; state of rules no longer in it is dropped.</param>
    public IReadOnlyList<RuleMatch> Evaluate(
        UpsReading? reading,
        UpsStatusChangedEventArgs? statusChange,
        bool connectionLost,
        IReadOnlyList<ActionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var now = _timeProvider.GetUtcNow();
        var matches = new List<RuleMatch>();
        var live = new HashSet<Guid>();

        foreach (var rule in rules)
        {
            if (!rule.Enabled || rule.UpsName is not null)
            {
                continue; // state of a rule that is off is forgotten below, so re-enabling starts fresh.
            }

            live.Add(rule.Id);
            var trigger = rule.Trigger;

            if (trigger.Kind is TriggerKind.FlagsBecameActive or TriggerKind.FlagsBecameInactive)
            {
                var became = trigger.Kind == TriggerKind.FlagsBecameActive;
                var changed = became ? statusChange?.NewlyActive : statusChange?.NewlyCleared;
                if (changed is { } flags && (flags & trigger.Flags) != UpsStatus.None)
                {
                    matches.Add(new RuleMatch(rule, $"ups.status flag(s) {flags & trigger.Flags} {(became ? "became active" : "became inactive")}."));
                }

                continue;
            }

            if (reading is null && trigger.Kind != TriggerKind.ConnectionLost)
            {
                continue;
            }

            var (holds, detail) = CheckCondition(trigger, reading, connectionLost);
            if (!_states.TryGetValue(rule.Id, out var state))
            {
                state = new RuleState();
                _states[rule.Id] = state;
            }

            if (!holds)
            {
                state.Since = null;
                state.Fired = false;
                continue;
            }

            state.Since ??= now;
            var required = TimeSpan.FromSeconds(
                Math.Max(0, rule.DelaySeconds)
                + (trigger.Kind is TriggerKind.OnBatteryFor or TriggerKind.ConnectionLost ? Math.Max(0, trigger.DurationSeconds) : 0));
            if (now - state.Since.Value < required)
            {
                continue;
            }

            if (rule.StopsComputer() || !state.Fired)
            {
                matches.Add(new RuleMatch(rule, detail, IsNewFiring: !state.Fired));
                state.Fired = true;
            }
        }

        foreach (var id in _states.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _states.Remove(id);
        }

        return matches;
    }

    private static (bool Holds, string Detail) CheckCondition(RuleTrigger trigger, UpsReading? reading, bool connectionLost)
    {
        switch (trigger.Kind)
        {
            case TriggerKind.FlagsActive:
                return trigger.Flags != UpsStatus.None && reading is not null && (reading.Status & trigger.Flags) == trigger.Flags
                    ? (true, $"ups.status contains {trigger.Flags}.")
                    : (false, string.Empty);

            case TriggerKind.ValueAtOrBelow:
            case TriggerKind.ValueAtOrAbove:
            {
                if (reading is null || (trigger.OnBatteryOnly && !reading.Status.HasFlag(UpsStatus.OB)))
                {
                    return (false, string.Empty);
                }

                if (GetValue(reading, trigger.Metric) is not double value)
                {
                    return (false, string.Empty); // unknown never matches.
                }

                var below = trigger.Kind == TriggerKind.ValueAtOrBelow;
                var matches = below ? value <= trigger.Threshold : value >= trigger.Threshold;
                return matches
                    ? (true, string.Create(CultureInfo.InvariantCulture,
                        $"{trigger.Metric} {value:0.##} is at or {(below ? "below" : "above")} {trigger.Threshold:0.##}."))
                    : (false, string.Empty);
            }

            case TriggerKind.OnBatteryFor:
                return reading is not null && reading.Status.HasFlag(UpsStatus.OB)
                    ? (true, $"The UPS has been on battery for at least {trigger.DurationSeconds}s.")
                    : (false, string.Empty);

            case TriggerKind.ConnectionLost:
                return connectionLost
                    ? (true, $"The connection to the NUT server has been lost for at least {trigger.DurationSeconds}s.")
                    : (false, string.Empty);

            default:
                return (false, string.Empty);
        }
    }

    private static double? GetValue(UpsReading reading, RuleMetric metric) => metric switch
    {
        RuleMetric.BatteryCharge => reading.BatteryCharge,
        RuleMetric.BatteryRuntimeSeconds => reading.BatteryRuntime?.TotalSeconds,
        RuleMetric.BatteryVoltage => reading.BatteryVoltage,
        RuleMetric.InputVoltage => reading.InputVoltage,
        RuleMetric.OutputVoltage => reading.OutputVoltage,
        RuleMetric.LoadPercent => reading.LoadPercent,
        RuleMetric.OutputPowerWatts => reading.OutputPowerWatts,
        _ => null,
    };
}
