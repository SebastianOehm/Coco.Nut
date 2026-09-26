namespace CocoNut.Core.Shutdown;

/// <summary>Why a <see cref="ShutdownDecision"/> starts or cancels the stop procedure.</summary>
public enum ShutdownReason
{
    /// <summary>No decision was made; the value carried by a <see cref="ShutdownDecisionKind.None"/> decision.</summary>
    None,

    /// <summary>Battery charge fell to or below <c>PowerSettings.BatteryChargeFloor</c> while on battery.</summary>
    BatteryChargeFloor,

    /// <summary>
    /// <c>ups.status</c> contains both <c>OB</c> and <c>LB</c> - NUT's own low-battery critical condition (the same
    /// one <c>upsmon</c> acts on). WinNUT never checked this and relied solely on the charge/runtime floors above,
    /// which can be missing or inaccurate; many UPS units report <c>LB</c> reliably even then.
    /// </summary>
    UpsLowBattery,

    /// <summary>Estimated runtime fell to or below <c>PowerSettings.RuntimeFloorSeconds</c> while on battery.</summary>
    RuntimeFloor,

    /// <summary>The NUT server requested a forced shutdown (<c>ups.status</c> contains <c>FSD</c>).</summary>
    ForcedShutdown,

    /// <summary>Mains power returned while a shutdown was pending.</summary>
    PowerRestored,
}
