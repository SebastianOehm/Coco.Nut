using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Actions;

/// <summary>The built-in rule set derived from <see cref="PowerSettings"/>; it reproduces the former fixed stop
/// conditions (ported from WinNUT's battery-floor and FSD handling, plus the OB+LB condition).</summary>
public static class DefaultRules
{
    /// <summary>Id of the forced-shutdown rule.</summary>
    public static readonly Guid ForcedShutdownId = new("c0c0a001-0000-4000-8000-000000000001");

    /// <summary>Id of the UPS low-battery rule.</summary>
    public static readonly Guid UpsLowBatteryId = new("c0c0a001-0000-4000-8000-000000000002");

    /// <summary>Id of the battery-charge-floor rule.</summary>
    public static readonly Guid BatteryChargeFloorId = new("c0c0a001-0000-4000-8000-000000000003");

    /// <summary>Id of the runtime-floor rule.</summary>
    public static readonly Guid RuntimeFloorId = new("c0c0a001-0000-4000-8000-000000000004");

    /// <summary>Whether <paramref name="id"/> is the id of one of the four built-in rules (reserved, user rules must not use it).</summary>
    public static bool IsBuiltIn(Guid id) =>
        id == ForcedShutdownId || id == UpsLowBatteryId || id == BatteryChargeFloorId || id == RuntimeFloorId;

    /// <summary>
    /// The effective rule list: <see cref="FromPowerSettings"/> followed by the usable user rules, so the built-in
    /// stop rules keep priority. A user rule is dropped (silently - this runs on every evaluation, and the settings
    /// validator already rejects such rules on save, so only a hand-edited file gets here) when it is
    /// <see langword="null"/>, has an empty or built-in or already used id, or fails
    /// <see cref="AppSettingsValidator.ValidateRule"/>. A user rule's stop reason is always
    /// <see cref="ShutdownReason.CustomRule"/>; a rule stored with another one is used as a copy that has it.
    /// </summary>
    public static IReadOnlyList<ActionRule> WithUserRules(PowerSettings power, ActionsSettings? actions)
    {
        var rules = new List<ActionRule>(FromPowerSettings(power));
        if (actions?.Rules is not { Count: > 0 } userRules)
        {
            return rules;
        }

        var ids = new HashSet<Guid>();
        foreach (var rule in userRules)
        {
            if (rule is null
                || rule.Id == Guid.Empty
                || IsBuiltIn(rule.Id)
                || AppSettingsValidator.ValidateRule(rule, string.Empty).Count > 0
                || !ids.Add(rule.Id))
            {
                continue;
            }

            rules.Add(rule.ShutdownReason == ShutdownReason.CustomRule ? rule : CopyWithCustomReason(rule));
        }

        return rules;
    }

    private static ActionRule CopyWithCustomReason(ActionRule rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        Enabled = rule.Enabled,
        UpsName = rule.UpsName,
        Trigger = rule.Trigger,
        DelaySeconds = rule.DelaySeconds,
        ShutdownReason = ShutdownReason.CustomRule,
        Actions = rule.Actions,
    };

    /// <summary>
    /// Builds the four built-in rules, in priority order: forced shutdown (only enabled when
    /// <see cref="PowerSettings.RespectFsd"/>), UPS low battery, charge floor, runtime floor. The result is fresh on
    /// every call; the ids are stable so a <see cref="RuleEngine"/> keeps its state across rebuilds.
    /// </summary>
    public static IReadOnlyList<ActionRule> FromPowerSettings(PowerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return
        [
            new ActionRule
            {
                Id = ForcedShutdownId,
                Name = "Forced shutdown (FSD)",
                Enabled = settings.RespectFsd,
                ShutdownReason = ShutdownReason.ForcedShutdown,
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.FSD },
                Actions = [RuleAction.StopComputer(settings.StopAction)],
            },

            // NUT's own critical condition (the one upsmon acts on): OB and LB together. WinNUT ignored this and
            // relied solely on the floors, which can be unavailable or wrong.
            new ActionRule
            {
                Id = UpsLowBatteryId,
                Name = "UPS low battery",
                ShutdownReason = ShutdownReason.UpsLowBattery,
                Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB | UpsStatus.LB },
                Actions = [RuleAction.StopComputer(settings.StopAction)],
            },
            new ActionRule
            {
                Id = BatteryChargeFloorId,
                Name = "Battery charge floor",
                ShutdownReason = ShutdownReason.BatteryChargeFloor,
                Trigger = new RuleTrigger
                {
                    Kind = TriggerKind.ValueAtOrBelow,
                    Metric = RuleMetric.BatteryCharge,
                    Threshold = settings.BatteryChargeFloor,
                    OnBatteryOnly = true,
                },
                Actions = [RuleAction.StopComputer(settings.StopAction)],
            },
            new ActionRule
            {
                Id = RuntimeFloorId,
                Name = "Runtime floor",
                ShutdownReason = ShutdownReason.RuntimeFloor,
                Trigger = new RuleTrigger
                {
                    Kind = TriggerKind.ValueAtOrBelow,
                    Metric = RuleMetric.BatteryRuntimeSeconds,
                    Threshold = settings.RuntimeFloorSeconds,
                    OnBatteryOnly = true,
                },
                Actions = [RuleAction.StopComputer(settings.StopAction)],
            },
        ];
    }
}
