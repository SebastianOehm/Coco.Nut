using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Actions;

/// <summary>
/// Ports the scenarios of the former <c>ShutdownPolicyTests</c>: the default rule set, run through the
/// <see cref="RuleEngine"/>, must decide exactly like the old fixed policy (the "power restored" cancel rule now
/// lives in the coordinator and is covered by <c>ShutdownCoordinatorTests</c>).
/// </summary>
public class DefaultRulesTests
{
    private static PowerSettings NewSettings(int chargeFloor = 30, int runtimeFloorSeconds = 120, bool respectFsd = false) => new()
    {
        BatteryChargeFloor = chargeFloor,
        RuntimeFloorSeconds = runtimeFloorSeconds,
        RespectFsd = respectFsd,
    };

    private static UpsReading Reading(UpsStatus status, double? charge = null, TimeSpan? runtime = null) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Status = status,
        BatteryCharge = charge,
        BatteryRuntime = runtime,
    };

    /// <summary>The reason the first firing stop rule reports, or <see cref="ShutdownReason.None"/>.</summary>
    private static ShutdownReason Decide(UpsReading reading, UpsStatusChangedEventArgs? change, PowerSettings settings)
    {
        var engine = new RuleEngine(new FakeTimeProvider());
        var matches = engine.Evaluate(reading, change, false, DefaultRules.FromPowerSettings(settings));
        return matches.FirstOrDefault(m => m.Rule.StopsComputer())?.Rule.ShutdownReason ?? ShutdownReason.None;
    }

    private static UpsStatusChangedEventArgs FsdAppears() =>
        new(UpsStatus.OL, UpsStatus.OL | UpsStatus.FSD, UpsStatus.FSD, UpsStatus.None);

    [Fact]
    public void OnBatteryAndLowBattery_StartsWithUpsLowBatteryReason()
    {
        // Charge/runtime are both comfortably above their floors - LB alone must still trigger the stop.
        var reading = Reading(UpsStatus.OB | UpsStatus.LB, charge: 90, runtime: TimeSpan.FromMinutes(30));

        Assert.Equal(ShutdownReason.UpsLowBattery, Decide(reading, null, NewSettings()));
    }

    [Fact]
    public void OnBatteryAndLowBattery_TakesPrecedenceOverFloorChecks()
    {
        var reading = Reading(UpsStatus.OB | UpsStatus.LB, charge: 1);

        Assert.Equal(ShutdownReason.UpsLowBattery, Decide(reading, null, NewSettings(chargeFloor: 30)));
    }

    [Fact]
    public void ChargeFloor_TakesPrecedenceOverRuntimeFloor()
    {
        var reading = Reading(UpsStatus.OB, charge: 1, runtime: TimeSpan.FromSeconds(1));

        Assert.Equal(ShutdownReason.BatteryChargeFloor, Decide(reading, null, NewSettings()));
    }

    [Fact]
    public void ForcedShutdown_TakesPrecedenceOverEverythingElse()
    {
        var reading = Reading(UpsStatus.OB | UpsStatus.LB | UpsStatus.FSD, charge: 1);
        var change = new UpsStatusChangedEventArgs(UpsStatus.OB | UpsStatus.LB, UpsStatus.OB | UpsStatus.LB | UpsStatus.FSD, UpsStatus.FSD, UpsStatus.None);

        Assert.Equal(ShutdownReason.ForcedShutdown, Decide(reading, change, NewSettings(respectFsd: true)));
    }

    [Fact]
    public void LowBatteryWithoutOnBattery_DoesNotStart()
    {
        var reading = Reading(UpsStatus.OL | UpsStatus.LB, charge: 90);

        Assert.Equal(ShutdownReason.None, Decide(reading, null, NewSettings()));
    }

    [Fact]
    public void OnBattery_ChargeAtOrBelowFloor_StartsWithBatteryChargeFloorReason()
    {
        Assert.Equal(ShutdownReason.BatteryChargeFloor, Decide(Reading(UpsStatus.OB, charge: 30), null, NewSettings(chargeFloor: 30)));
    }

    [Fact]
    public void OnBattery_ChargeAboveFloor_DoesNotStart()
    {
        Assert.Equal(ShutdownReason.None, Decide(Reading(UpsStatus.OB, charge: 31), null, NewSettings(chargeFloor: 30)));
    }

    [Fact]
    public void OnBattery_RuntimeAtOrBelowFloor_StartsWithRuntimeFloorReason()
    {
        var reading = Reading(UpsStatus.OB, charge: 90, runtime: TimeSpan.FromSeconds(120));

        Assert.Equal(ShutdownReason.RuntimeFloor, Decide(reading, null, NewSettings(runtimeFloorSeconds: 120)));
    }

    [Fact]
    public void OnBattery_UnknownChargeDoesNotSuppressRuntimeCheck()
    {
        var reading = Reading(UpsStatus.OB, charge: null, runtime: TimeSpan.FromSeconds(60));

        Assert.Equal(ShutdownReason.RuntimeFloor, Decide(reading, null, NewSettings(runtimeFloorSeconds: 120)));
    }

    [Fact]
    public void OnBattery_BothValuesUnknown_DoesNotStart()
    {
        Assert.Equal(ShutdownReason.None, Decide(Reading(UpsStatus.OB), null, NewSettings()));
    }

    [Fact]
    public void NotOnBattery_NeverStartsFromBatteryFloors()
    {
        var reading = Reading(UpsStatus.OL, charge: 0, runtime: TimeSpan.Zero);

        Assert.Equal(ShutdownReason.None, Decide(reading, null, NewSettings()));
    }

    [Fact]
    public void FsdNewlyActiveAndRespectFsd_Starts()
    {
        Assert.Equal(ShutdownReason.ForcedShutdown, Decide(Reading(UpsStatus.OL | UpsStatus.FSD), FsdAppears(), NewSettings(respectFsd: true)));
    }

    [Fact]
    public void FsdNewlyActiveButRespectFsdDisabled_DoesNotStart()
    {
        Assert.Equal(ShutdownReason.None, Decide(Reading(UpsStatus.OL | UpsStatus.FSD), FsdAppears(), NewSettings(respectFsd: false)));
    }

    [Fact]
    public void FsdStillActiveButNotNewlyActive_DoesNotStartAgain()
    {
        var change = new UpsStatusChangedEventArgs(UpsStatus.OL | UpsStatus.FSD, UpsStatus.OL | UpsStatus.FSD, UpsStatus.None, UpsStatus.None);

        Assert.Equal(ShutdownReason.None, Decide(Reading(UpsStatus.OL | UpsStatus.FSD), change, NewSettings(respectFsd: true)));
    }

    [Fact]
    public void FsdWithoutAnyStatusChange_DoesNotStart()
    {
        Assert.Equal(ShutdownReason.None, Decide(Reading(UpsStatus.OL | UpsStatus.FSD), null, NewSettings(respectFsd: true)));
    }

    [Fact]
    public void FromPowerSettings_BuildsTheFourRulesInPriorityOrderWithStableIds()
    {
        var settings = NewSettings(chargeFloor: 25, runtimeFloorSeconds: 90, respectFsd: true);
        settings.StopAction = StopAction.Hibernate;

        var rules = DefaultRules.FromPowerSettings(settings);

        Assert.Equal(
            [DefaultRules.ForcedShutdownId, DefaultRules.UpsLowBatteryId, DefaultRules.BatteryChargeFloorId, DefaultRules.RuntimeFloorId],
            rules.Select(r => r.Id).ToArray());
        Assert.Equal(
            [ShutdownReason.ForcedShutdown, ShutdownReason.UpsLowBattery, ShutdownReason.BatteryChargeFloor, ShutdownReason.RuntimeFloor],
            rules.Select(r => r.ShutdownReason).ToArray());
        Assert.All(rules, r =>
        {
            Assert.True(r.Enabled);
            Assert.Null(r.UpsName);
            Assert.Equal(0, r.DelaySeconds);
            var action = Assert.Single(r.Actions);
            Assert.Equal(ActionKind.StopComputer, action.Kind);
            Assert.Equal(StopAction.Hibernate, action.StopAction);
        });
        Assert.Equal(25, rules[2].Trigger.Threshold);
        Assert.Equal(90, rules[3].Trigger.Threshold);
        Assert.True(rules[2].Trigger.OnBatteryOnly);
        Assert.True(rules[3].Trigger.OnBatteryOnly);
        Assert.Equal(DefaultRules.ForcedShutdownId, DefaultRules.FromPowerSettings(settings)[0].Id);
    }

    [Fact]
    public void FromPowerSettings_ForcedShutdownRuleFollowsRespectFsd()
    {
        Assert.False(DefaultRules.FromPowerSettings(NewSettings(respectFsd: false))[0].Enabled);
        Assert.True(DefaultRules.FromPowerSettings(NewSettings(respectFsd: true))[0].Enabled);
    }

    [Fact]
    public void RebuildingTheRulesEveryEvaluation_StillReportsTheSameStopConditionEachTime()
    {
        // Mirrors how the coordinator is wired: a fresh rule list per evaluation, one engine.
        var engine = new RuleEngine(new FakeTimeProvider());
        var settings = NewSettings();
        var reading = Reading(UpsStatus.OB, charge: 5);

        for (var i = 0; i < 3; i++)
        {
            var matches = engine.Evaluate(reading, null, false, DefaultRules.FromPowerSettings(settings));
            Assert.Equal(DefaultRules.BatteryChargeFloorId, matches[0].Rule.Id);
        }
    }

    // ---- the effective rule list: built-ins + user rules ----

    private static ActionRule UserRule(string name = "mine", Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB },
        Actions = [RuleAction.Notify("on battery")],
    };

    [Fact]
    public void WithUserRules_PutsTheBuiltInsFirst_ThenTheUserRulesInOrder()
    {
        var first = UserRule("first");
        var second = UserRule("second");

        var rules = DefaultRules.WithUserRules(NewSettings(), new ActionsSettings { Rules = [first, second] });

        Assert.Equal(6, rules.Count);
        Assert.All(rules.Take(4), rule => Assert.True(DefaultRules.IsBuiltIn(rule.Id)));
        Assert.Same(first, rules[4]);
        Assert.Same(second, rules[5]);
    }

    [Fact]
    public void WithUserRules_WithoutUserRules_IsJustTheBuiltIns()
    {
        Assert.Equal(4, DefaultRules.WithUserRules(NewSettings(), new ActionsSettings()).Count);
        Assert.Equal(4, DefaultRules.WithUserRules(NewSettings(), null).Count);
        Assert.Equal(4, DefaultRules.WithUserRules(NewSettings(), new ActionsSettings { Rules = null! }).Count);
    }

    [Fact]
    public void WithUserRules_DropsRulesThatUseABuiltInOrEmptyOrRepeatedId_OrAreNullOrInvalid()
    {
        var kept = UserRule("kept");
        var invalid = UserRule("no actions");
        invalid.Actions = [];

        var rules = DefaultRules.WithUserRules(NewSettings(), new ActionsSettings
        {
            Rules =
            [
                UserRule("hijack", DefaultRules.ForcedShutdownId),
                UserRule("empty id", Guid.Empty),
                null!,
                invalid,
                kept,
                UserRule("same id again", kept.Id),
            ],
        });

        Assert.Equal(5, rules.Count);
        Assert.Same(kept, rules[4]);
    }

    [Fact]
    public void WithUserRules_ForcesTheCustomRuleReason_WithoutTouchingTheStoredRule()
    {
        var stored = UserRule("pretender");
        stored.ShutdownReason = ShutdownReason.ForcedShutdown;

        var rule = DefaultRules.WithUserRules(NewSettings(), new ActionsSettings { Rules = [stored] })[4];

        Assert.Equal(ShutdownReason.CustomRule, rule.ShutdownReason);
        Assert.Equal(stored.Id, rule.Id);
        Assert.Equal("pretender", rule.Name);
        Assert.Equal(ShutdownReason.ForcedShutdown, stored.ShutdownReason);
    }

    [Fact]
    public void IsBuiltIn_IsTrueForTheFourBuiltInIdsOnly()
    {
        Assert.All(DefaultRules.FromPowerSettings(NewSettings()), rule => Assert.True(DefaultRules.IsBuiltIn(rule.Id)));
        Assert.False(DefaultRules.IsBuiltIn(Guid.NewGuid()));
        Assert.False(DefaultRules.IsBuiltIn(Guid.Empty));
    }
}
