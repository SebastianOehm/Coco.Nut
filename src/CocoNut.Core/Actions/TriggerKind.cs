namespace CocoNut.Core.Actions;

/// <summary>What makes an <see cref="ActionRule"/> fire. Level triggers describe a condition that holds for a while,
/// edge triggers describe the moment something changes.</summary>
public enum TriggerKind
{
    /// <summary>Level: every flag in <see cref="RuleTrigger.Flags"/> is present in <c>ups.status</c>.</summary>
    FlagsActive,

    /// <summary>Edge: any flag in <see cref="RuleTrigger.Flags"/> became active with the last status change.</summary>
    FlagsBecameActive,

    /// <summary>Edge: any flag in <see cref="RuleTrigger.Flags"/> became inactive with the last status change.</summary>
    FlagsBecameInactive,

    /// <summary>Level: <see cref="RuleTrigger.Metric"/> is at or below <see cref="RuleTrigger.Threshold"/> (inclusive).</summary>
    ValueAtOrBelow,

    /// <summary>Level: <see cref="RuleTrigger.Metric"/> is at or above <see cref="RuleTrigger.Threshold"/> (inclusive).</summary>
    ValueAtOrAbove,

    /// <summary>Level: the UPS has been on battery (<c>OB</c>) continuously for at least <see cref="RuleTrigger.DurationSeconds"/>.</summary>
    OnBatteryFor,

    /// <summary>Level: the monitor's connection to the NUT server has been lost for at least <see cref="RuleTrigger.DurationSeconds"/>.</summary>
    ConnectionLost,
}
