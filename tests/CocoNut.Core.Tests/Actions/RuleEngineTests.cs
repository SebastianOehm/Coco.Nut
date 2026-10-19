using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Actions;

public class RuleEngineTests
{
    private readonly FakeTimeProvider _time = new();

    private RuleEngine NewEngine() => new(_time);

    private static UpsReading Reading(
        UpsStatus status,
        double? charge = null,
        TimeSpan? runtime = null,
        double? load = null,
        double? inputVoltage = null) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Status = status,
        BatteryCharge = charge,
        BatteryRuntime = runtime,
        LoadPercent = load,
        InputVoltage = inputVoltage,
    };

    private static UpsStatusChangedEventArgs Change(UpsStatus previous, UpsStatus current) =>
        new(previous, current, current & ~previous, previous & ~current);

    private static ActionRule Rule(RuleTrigger trigger, int delay = 0, bool stop = false, bool enabled = true) => new()
    {
        Name = "test",
        Enabled = enabled,
        DelaySeconds = delay,
        Trigger = trigger,
        Actions = stop ? [RuleAction.StopComputer(StopAction.Shutdown)] : [],
    };

    private static RuleTrigger Value(TriggerKind kind, RuleMetric metric, double threshold, bool onBattery = false) =>
        new() { Kind = kind, Metric = metric, Threshold = threshold, OnBatteryOnly = onBattery };

    private static int Count(RuleEngine engine, ActionRule rule, UpsReading? reading, UpsStatusChangedEventArgs? change = null, bool connectionLost = false) =>
        engine.Evaluate(reading, change, connectionLost, [rule]).Count;

    // ---- trigger kinds ----

    [Fact]
    public void FlagsActive_NeedsAllFlags()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB | UpsStatus.LB });

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL | UpsStatus.LB)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB | UpsStatus.LB | UpsStatus.DISCHRG)));
    }

    [Fact]
    public void FlagsActive_WithNoFlags_NeverMatches()
    {
        Assert.Equal(0, Count(NewEngine(), Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive }), Reading(UpsStatus.OB)));
    }

    [Fact]
    public void FlagsBecameActive_FiresOnlyOnTheEdge_AndEveryTimeItOccurs()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.FSD | UpsStatus.OFF });
        var withFsd = Reading(UpsStatus.OL | UpsStatus.FSD);

        // No status change: nothing, even though the flag is present.
        Assert.Equal(0, Count(engine, rule, withFsd));
        // Already active, not newly active.
        Assert.Equal(0, Count(engine, rule, withFsd, Change(UpsStatus.OL | UpsStatus.FSD, UpsStatus.OL | UpsStatus.FSD | UpsStatus.CHRG)));
        // The edge (any of the flags).
        Assert.Equal(1, Count(engine, rule, withFsd, Change(UpsStatus.OL, UpsStatus.OL | UpsStatus.FSD)));
        // And again for a second occurrence.
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL), Change(UpsStatus.OL | UpsStatus.FSD, UpsStatus.OL)));
        Assert.Equal(1, Count(engine, rule, withFsd, Change(UpsStatus.OL, UpsStatus.OL | UpsStatus.FSD)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OL | UpsStatus.OFF), Change(UpsStatus.OL, UpsStatus.OL | UpsStatus.OFF)));
    }

    [Fact]
    public void FlagsBecameInactive_FiresWhenAFlagIsCleared()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsBecameInactive, Flags = UpsStatus.OB });

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB | UpsStatus.LB), Change(UpsStatus.OB, UpsStatus.OB | UpsStatus.LB)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OL), Change(UpsStatus.OB, UpsStatus.OL)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
    }

    [Fact]
    public void ValueAtOrBelow_IsInclusive()
    {
        var engine = NewEngine();
        var rule = Rule(Value(TriggerKind.ValueAtOrBelow, RuleMetric.BatteryCharge, 30));

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL, charge: 31)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OL, charge: 30)));
    }

    [Fact]
    public void ValueAtOrAbove_IsInclusive()
    {
        var engine = NewEngine();
        var rule = Rule(Value(TriggerKind.ValueAtOrAbove, RuleMetric.LoadPercent, 80));

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL, load: 79.9)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OL, load: 80)));
    }

    [Theory]
    [InlineData(TriggerKind.ValueAtOrBelow)]
    [InlineData(TriggerKind.ValueAtOrAbove)]
    public void Value_NullNeverMatches(TriggerKind kind)
    {
        var engine = NewEngine();
        var rule = Rule(Value(kind, RuleMetric.BatteryCharge, 50));

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL, charge: null)));
    }

    [Fact]
    public void Metrics_MapToTheReading()
    {
        var reading = new UpsReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = UpsStatus.OL,
            BatteryCharge = 1,
            BatteryRuntime = TimeSpan.FromSeconds(2),
            BatteryVoltage = 3,
            InputVoltage = 4,
            OutputVoltage = 5,
            LoadPercent = 6,
            OutputPowerWatts = 7,
        };

        foreach (var (metric, expected) in new[]
        {
            (RuleMetric.BatteryCharge, 1.0), (RuleMetric.BatteryRuntimeSeconds, 2.0), (RuleMetric.BatteryVoltage, 3.0),
            (RuleMetric.InputVoltage, 4.0), (RuleMetric.OutputVoltage, 5.0), (RuleMetric.LoadPercent, 6.0),
            (RuleMetric.OutputPowerWatts, 7.0),
        })
        {
            var engine = NewEngine();
            Assert.Equal(1, Count(engine, Rule(Value(TriggerKind.ValueAtOrBelow, metric, expected)), reading));
            Assert.Equal(0, Count(engine, Rule(Value(TriggerKind.ValueAtOrBelow, metric, expected - 0.5)), reading));
        }
    }

    [Fact]
    public void Value_OnBatteryOnly_RequiresOb()
    {
        var engine = NewEngine();
        var rule = Rule(Value(TriggerKind.ValueAtOrBelow, RuleMetric.BatteryCharge, 30, onBattery: true));

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL, charge: 0)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB, charge: 0)));
    }

    [Fact]
    public void Value_WithoutOnBatteryOnly_MatchesOnLineToo()
    {
        var rule = Rule(Value(TriggerKind.ValueAtOrBelow, RuleMetric.BatteryCharge, 30));

        Assert.Equal(1, Count(NewEngine(), rule, Reading(UpsStatus.OL, charge: 0)));
    }

    [Fact]
    public void OnBatteryFor_FiresOnceOnBatteryForTheDuration_AndRestartsAfterPowerReturns()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.OnBatteryFor, DurationSeconds = 60 });
        var onBattery = Reading(UpsStatus.OB);

        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Count(engine, rule, onBattery));

        // Level, non-stop rule: once per rising edge.
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, Count(engine, rule, onBattery));

        // Power returns, then a new outage starts the clock again.
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(1, Count(engine, rule, onBattery));
    }

    [Fact]
    public void ConnectionLost_FiresAfterTheDuration_EvenWithoutAReading()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 30 });

        Assert.Equal(0, Count(engine, rule, null, connectionLost: true));
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, Count(engine, rule, null, connectionLost: true));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Count(engine, rule, null, connectionLost: true));
    }

    [Fact]
    public void ConnectionLost_ResetsWhenTheConnectionReturns()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 30 });

        Count(engine, rule, null, connectionLost: true);
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL), connectionLost: false));
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, Count(engine, rule, null, connectionLost: true)); // clock restarted just now.
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, Count(engine, rule, null, connectionLost: true));
    }

    [Fact]
    public void WithoutAReading_OtherLevelRulesDoNotFire_AndKeepTheirState()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, delay: 10);

        Count(engine, rule, Reading(UpsStatus.OB));
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(0, Count(engine, rule, null, connectionLost: true));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB))); // the clock kept running.
    }

    // ---- delay ----

    [Fact]
    public void Delay_ConditionMustHoldContinuously_AndResetsWhenItBreaks()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, delay: 30);
        var onBattery = Reading(UpsStatus.OB);

        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, Count(engine, rule, onBattery));

        // Condition breaks: the sustain clock starts over.
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, Count(engine, rule, onBattery));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Count(engine, rule, onBattery));
    }

    [Fact]
    public void Delay_ZeroFiresAtOnce()
    {
        Assert.Equal(1, Count(NewEngine(), Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }), Reading(UpsStatus.OB)));
    }

    [Fact]
    public void Delay_IsIgnoredForEdgeTriggers()
    {
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.OB }, delay: 600);

        Assert.Equal(1, Count(NewEngine(), rule, Reading(UpsStatus.OB), Change(UpsStatus.OL, UpsStatus.OB)));
    }

    [Fact]
    public void Delay_AddsToTheDurationOfDurationTriggers()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.OnBatteryFor, DurationSeconds = 10 }, delay: 5);

        Count(engine, rule, Reading(UpsStatus.OB));
        _time.Advance(TimeSpan.FromSeconds(14));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
    }

    // ---- level vs. stop-rule semantics ----

    [Fact]
    public void NonStopLevelRule_FiresOncePerRisingEdge()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB });

        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
    }

    [Fact]
    public void StopRule_ReportsOnEveryEvaluationWhileTheConditionHolds()
    {
        // The shutdown coordinator relies on this: it ignores repeats while pending / re-arming, but must be told
        // again once it re-arms and the condition still holds.
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, stop: true);

        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
    }

    [Fact]
    public void StopRule_WithDelay_ReportsEveryTimeAfterTheDelayHasPassed()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, delay: 30, stop: true);

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
    }

    [Fact]
    public void StopRule_EdgeTrigger_FiresOnlyWhenTheEdgeOccurs()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.FSD }, stop: true);

        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.FSD), Change(UpsStatus.OL, UpsStatus.FSD)));
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.FSD)));
    }

    // ---- disabled / scoped rules, order, state keyed by id ----

    [Fact]
    public void DisabledRule_NeverFires()
    {
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, enabled: false);

        Assert.Equal(0, Count(NewEngine(), rule, Reading(UpsStatus.OB)));
    }

    [Fact]
    public void DisabledRule_ForgetsItsState_SoReEnablingStartsTheDelayFresh()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, delay: 30);

        Count(engine, rule, Reading(UpsStatus.OB));
        _time.Advance(TimeSpan.FromSeconds(30));
        rule.Enabled = false;
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        rule.Enabled = true;
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB))); // clock restarted now.
    }

    [Fact]
    public void RuleScopedToANamedUps_IsIgnoredForNow()
    {
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB });
        rule.UpsName = "other";

        Assert.Equal(0, Count(NewEngine(), rule, Reading(UpsStatus.OB)));
    }

    [Fact]
    public void MatchesAreReturnedInRuleOrder()
    {
        var engine = NewEngine();
        var first = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB });
        var second = Rule(Value(TriggerKind.ValueAtOrBelow, RuleMetric.BatteryCharge, 50));
        var third = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.LB });

        var matches = engine.Evaluate(Reading(UpsStatus.OB | UpsStatus.LB, charge: 10), null, false, [first, second, third]);

        Assert.Equal([first, second, third], matches.Select(m => m.Rule).ToArray());
        Assert.All(matches, m => Assert.False(string.IsNullOrWhiteSpace(m.Detail)));
    }

    [Fact]
    public void State_IsKeyedByRuleId_SoRebuiltRuleInstancesKeepTheirClock()
    {
        var engine = NewEngine();
        var id = Guid.NewGuid();
        ActionRule Build() => new()
        {
            Id = id,
            Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB },
            DelaySeconds = 30,
        };

        Assert.Equal(0, Count(engine, Build(), Reading(UpsStatus.OB)));
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, Count(engine, Build(), Reading(UpsStatus.OB)));
    }

    [Fact]
    public void RuleRemovedFromTheSet_LosesItsState()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB });

        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB)));
        engine.Evaluate(Reading(UpsStatus.OB), null, false, []);
        Assert.Equal(1, Count(engine, rule, Reading(UpsStatus.OB))); // fired flag was dropped with the rule.
    }

    // ---- new firing vs. repeated report ----

    private static RuleMatch Single(RuleEngine engine, ActionRule rule, UpsReading? reading, UpsStatusChangedEventArgs? change = null) =>
        Assert.Single(engine.Evaluate(reading, change, false, [rule]));

    [Fact]
    public void NonStopLevelRule_IsANewFiring()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB });

        Assert.True(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
    }

    [Fact]
    public void StopRule_IsANewFiringOnTheRisingEdgeOnly_RepeatsAreNot()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, stop: true);

        Assert.True(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
        Assert.False(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
        Assert.False(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);

        // The condition clears and returns: a new rising edge.
        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OL)));
        Assert.True(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
    }

    [Fact]
    public void StopRule_WithDelay_IsANewFiringWhenTheDelayElapses_NotEarlierNorLater()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB }, delay: 30, stop: true);

        Assert.Equal(0, Count(engine, rule, Reading(UpsStatus.OB)));
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
        Assert.False(Single(engine, rule, Reading(UpsStatus.OB)).IsNewFiring);
    }

    [Fact]
    public void EdgeTrigger_EveryOccurrenceIsANewFiring()
    {
        var engine = NewEngine();
        var rule = Rule(new RuleTrigger { Kind = TriggerKind.FlagsBecameActive, Flags = UpsStatus.FSD }, stop: true);

        Assert.True(Single(engine, rule, Reading(UpsStatus.FSD), Change(UpsStatus.OL, UpsStatus.FSD)).IsNewFiring);
        Assert.True(Single(engine, rule, Reading(UpsStatus.FSD), Change(UpsStatus.OL, UpsStatus.FSD)).IsNewFiring);
    }
}
