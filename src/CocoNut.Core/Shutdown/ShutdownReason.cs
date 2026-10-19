namespace CocoNut.Core.Shutdown;

/// <summary>Why the stop procedure starts or is cancelled.</summary>
public enum ShutdownReason
{
    /// <summary>No reason (default value).</summary>
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

    /// <summary>
    /// A user-defined rule (not one of the built-in ones above) started the stop. Has no dedicated localized text
    /// yet; the UI shows no specific reason for it.
    /// </summary>
    CustomRule,
}
