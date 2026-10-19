namespace CocoNut.Core.Actions;

/// <summary>A numeric UPS value a <see cref="TriggerKind.ValueAtOrBelow"/>/<see cref="TriggerKind.ValueAtOrAbove"/>
/// trigger compares against its threshold. Each maps to a property of <see cref="Ups.UpsReading"/>.</summary>
public enum RuleMetric
{
    /// <summary>Battery charge in percent.</summary>
    BatteryCharge,

    /// <summary>Estimated battery runtime in seconds.</summary>
    BatteryRuntimeSeconds,

    /// <summary>Battery voltage in volts.</summary>
    BatteryVoltage,

    /// <summary>Input voltage in volts.</summary>
    InputVoltage,

    /// <summary>Output voltage in volts.</summary>
    OutputVoltage,

    /// <summary>Load in percent.</summary>
    LoadPercent,

    /// <summary>Output power in watts.</summary>
    OutputPowerWatts,
}
