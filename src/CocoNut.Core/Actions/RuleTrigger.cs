using CocoNut.Core.Ups;

namespace CocoNut.Core.Actions;

/// <summary>The condition of an <see cref="ActionRule"/>. Which properties matter depends on <see cref="Kind"/>;
/// the others are ignored.</summary>
public sealed class RuleTrigger
{
    /// <summary>What kind of condition this is.</summary>
    public TriggerKind Kind { get; set; } = TriggerKind.FlagsActive;

    /// <summary>The <c>ups.status</c> flags for the flag triggers.</summary>
    public UpsStatus Flags { get; set; }

    /// <summary>The compared value for <see cref="TriggerKind.ValueAtOrBelow"/>/<see cref="TriggerKind.ValueAtOrAbove"/>.</summary>
    public RuleMetric Metric { get; set; }

    /// <summary>The threshold (inclusive) for the value triggers, in the unit of <see cref="Metric"/>.</summary>
    public double Threshold { get; set; }

    /// <summary>Value triggers only: the condition only matches while <c>ups.status</c> contains <c>OB</c>.</summary>
    public bool OnBatteryOnly { get; set; }

    /// <summary>How long (seconds) the condition must hold, for <see cref="TriggerKind.OnBatteryFor"/> and
    /// <see cref="TriggerKind.ConnectionLost"/>. Added to <see cref="ActionRule.DelaySeconds"/>.</summary>
    public int DurationSeconds { get; set; }
}
