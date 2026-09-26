using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Shutdown;

/// <summary>
/// Pure decision logic for whether the stop procedure should start or be cancelled, ported from WinNUT's
/// <c>WinNUT.vb</c>: the battery-floor check in <c>Update_UPS_Data</c> (~line 620-633) and the FSD / back-online
/// handling in <c>HandleUPSStatusChange</c> (~line 927-947).
/// </summary>
public static class ShutdownPolicy
{
    /// <summary>
    /// Evaluates one poll result. Call this from both <c>UpsMonitor.ReadingUpdated</c> (with
    /// <paramref name="statusChange"/> <see langword="null"/>) and <c>UpsMonitor.StatusChanged</c> (with the
    /// event's arguments), so both the level-triggered battery-floor rule and the edge-triggered FSD/back-online
    /// rules are evaluated on every relevant event.
    /// </summary>
    /// <param name="reading">The poll result to evaluate.</param>
    /// <param name="statusChange">
    /// The <c>StatusChanged</c> event's arguments when this call is in response to that event; <see langword="null"/>
    /// when called for a plain <c>ReadingUpdated</c> event. Only <see cref="UpsStatusChangedEventArgs.NewlyActive"/>
    /// is used, to detect FSD becoming active exactly once (WinNUT only ever saw newly-active flags here).
    /// </param>
    /// <param name="settings">The current power settings (floors, FSD handling).</param>
    /// <param name="shutdownPending">
    /// Whether the stop procedure is already running. While pending, only the "power restored" cancel rule is
    /// evaluated - the stop procedure is never started a second time (WinNUT: <c>Not ShutdownStatus</c> guards the
    /// battery-floor check, and the back-online check is only meaningful while <c>ShutdownStatus</c>).
    /// </param>
    public static ShutdownDecision Evaluate(
        UpsReading reading,
        UpsStatusChangedEventArgs? statusChange,
        PowerSettings settings,
        bool shutdownPending)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(settings);

        if (shutdownPending)
        {
            var backOnline = reading.Status.HasFlag(UpsStatus.OL) && !reading.Status.HasFlag(UpsStatus.OB);
            return backOnline
                ? new ShutdownDecision(ShutdownDecisionKind.Cancel, ShutdownReason.PowerRestored,
                    "Mains power (OL) is present again and on-battery (OB) has cleared.")
                : ShutdownDecision.None;
        }

        if (settings.RespectFsd && (statusChange?.NewlyActive.HasFlag(UpsStatus.FSD) ?? false))
        {
            return new ShutdownDecision(ShutdownDecisionKind.Start, ShutdownReason.ForcedShutdown,
                "The NUT server requested a forced shutdown (ups.status contains FSD).");
        }

        // NUT's own critical condition (the one upsmon itself acts on): OB and LB together. WinNUT ignored this and
        // relied solely on the floors below, which can be unavailable or wrong; many UPS units report LB reliably
        // even then, so this is checked before them.
        if (reading.Status.HasFlag(UpsStatus.OB) && reading.Status.HasFlag(UpsStatus.LB))
        {
            return new ShutdownDecision(ShutdownDecisionKind.Start, ShutdownReason.UpsLowBattery,
                "The UPS reports a low battery condition (ups.status contains OB and LB).");
        }

        if (reading.Status.HasFlag(UpsStatus.OB))
        {
            // WinNUT: "(.Batt_Charge <> -1 AndAlso .Batt_Charge <= Floor) Or (.Batt_Runtime <> -1 AndAlso .Batt_Runtime <= Floor)".
            // A null (unknown) value never triggers its own condition, but does not suppress the other one either.
            if (reading.BatteryCharge is double charge && charge <= settings.BatteryChargeFloor)
            {
                return new ShutdownDecision(ShutdownDecisionKind.Start, ShutdownReason.BatteryChargeFloor,
                    $"Battery charge {charge:0.#}% is at or below the floor of {settings.BatteryChargeFloor}%.");
            }

            if (reading.BatteryRuntime is { } runtime && runtime.TotalSeconds <= settings.RuntimeFloorSeconds)
            {
                return new ShutdownDecision(ShutdownDecisionKind.Start, ShutdownReason.RuntimeFloor,
                    $"Estimated runtime {runtime.TotalSeconds:0}s is at or below the floor of {settings.RuntimeFloorSeconds}s.");
            }
        }

        return ShutdownDecision.None;
    }
}
