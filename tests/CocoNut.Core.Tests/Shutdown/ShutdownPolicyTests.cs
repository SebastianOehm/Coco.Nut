using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Tests.Shutdown;

public class ShutdownPolicyTests
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

    [Fact]
    public void Evaluate_OnBatteryAndLowBattery_StartsWithUpsLowBatteryReason()
    {
        // Charge/runtime are both comfortably above their floors - LB alone must still trigger the stop.
        var reading = Reading(UpsStatus.OB | UpsStatus.LB, charge: 90, runtime: TimeSpan.FromMinutes(30));

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.Start, decision.Kind);
        Assert.Equal(ShutdownReason.UpsLowBattery, decision.Reason);
    }

    [Fact]
    public void Evaluate_OnBatteryAndLowBattery_TakesPrecedenceOverFloorChecks()
    {
        // Even when a floor is also breached, LB is checked first and wins (the two aren't mutually exclusive, so
        // this just fixes which single reason is reported).
        var reading = Reading(UpsStatus.OB | UpsStatus.LB, charge: 1);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(chargeFloor: 30), shutdownPending: false);

        Assert.Equal(ShutdownReason.UpsLowBattery, decision.Reason);
    }

    [Fact]
    public void Evaluate_LowBatteryWithoutOnBattery_DoesNotStart()
    {
        // LB without OB (e.g. a battery test) is not the "running out of power on battery" condition.
        var reading = Reading(UpsStatus.OL | UpsStatus.LB, charge: 90);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_OnBattery_ChargeAtOrBelowFloor_StartsWithBatteryChargeFloorReason()
    {
        var reading = Reading(UpsStatus.OB, charge: 30);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(chargeFloor: 30), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.Start, decision.Kind);
        Assert.Equal(ShutdownReason.BatteryChargeFloor, decision.Reason);
    }

    [Fact]
    public void Evaluate_OnBattery_ChargeAboveFloor_DoesNotStart()
    {
        var reading = Reading(UpsStatus.OB, charge: 31);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(chargeFloor: 30), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_OnBattery_RuntimeAtOrBelowFloor_StartsWithRuntimeFloorReason()
    {
        var reading = Reading(UpsStatus.OB, charge: 90, runtime: TimeSpan.FromSeconds(120));

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(runtimeFloorSeconds: 120), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.Start, decision.Kind);
        Assert.Equal(ShutdownReason.RuntimeFloor, decision.Reason);
    }

    [Fact]
    public void Evaluate_OnBattery_UnknownChargeDoesNotSuppressRuntimeCheck()
    {
        // WinNUT: "(.Batt_Charge <> -1 AndAlso ...) Or (.Batt_Runtime <> -1 AndAlso ...)" - an unavailable (null)
        // charge simply drops out of the Or, it never blocks the runtime condition from firing.
        var reading = Reading(UpsStatus.OB, charge: null, runtime: TimeSpan.FromSeconds(60));

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(runtimeFloorSeconds: 120), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.Start, decision.Kind);
        Assert.Equal(ShutdownReason.RuntimeFloor, decision.Reason);
    }

    [Fact]
    public void Evaluate_OnBattery_BothValuesUnknown_DoesNotStart()
    {
        var reading = Reading(UpsStatus.OB, charge: null, runtime: null);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_NotOnBattery_NeverStartsFromBatteryFloors()
    {
        var reading = Reading(UpsStatus.OL, charge: 0, runtime: TimeSpan.Zero);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_FsdNewlyActiveAndRespectFsd_Starts()
    {
        var reading = Reading(UpsStatus.OL | UpsStatus.FSD);
        var change = new UpsStatusChangedEventArgs(UpsStatus.OL, UpsStatus.OL | UpsStatus.FSD, UpsStatus.FSD, UpsStatus.None);

        var decision = ShutdownPolicy.Evaluate(reading, change, NewSettings(respectFsd: true), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.Start, decision.Kind);
        Assert.Equal(ShutdownReason.ForcedShutdown, decision.Reason);
    }

    [Fact]
    public void Evaluate_FsdNewlyActiveButRespectFsdDisabled_DoesNotStart()
    {
        var reading = Reading(UpsStatus.OL | UpsStatus.FSD);
        var change = new UpsStatusChangedEventArgs(UpsStatus.OL, UpsStatus.OL | UpsStatus.FSD, UpsStatus.FSD, UpsStatus.None);

        var decision = ShutdownPolicy.Evaluate(reading, change, NewSettings(respectFsd: false), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_FsdStillActiveButNotNewlyActive_DoesNotStartAgain()
    {
        // FSD was already active before this poll; only a *newly* active FSD should trigger the stop procedure.
        var reading = Reading(UpsStatus.OL | UpsStatus.FSD);
        var change = new UpsStatusChangedEventArgs(UpsStatus.OL | UpsStatus.FSD, UpsStatus.OL | UpsStatus.FSD, UpsStatus.None, UpsStatus.None);

        var decision = ShutdownPolicy.Evaluate(reading, change, NewSettings(respectFsd: true), shutdownPending: false);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_ShutdownPending_NeverStartsAgain_EvenWhenFloorsOrFsdApply()
    {
        var reading = Reading(UpsStatus.OB, charge: 1);
        var change = new UpsStatusChangedEventArgs(UpsStatus.OB, UpsStatus.OB | UpsStatus.FSD, UpsStatus.FSD, UpsStatus.None);

        var decision = ShutdownPolicy.Evaluate(reading, change, NewSettings(respectFsd: true), shutdownPending: true);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_ShutdownPending_MainsRestoredAndNotOnBattery_Cancels()
    {
        var reading = Reading(UpsStatus.OL);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: true);

        Assert.Equal(ShutdownDecisionKind.Cancel, decision.Kind);
        Assert.Equal(ShutdownReason.PowerRestored, decision.Reason);
    }

    [Fact]
    public void Evaluate_ShutdownPending_OnLineButStillOnBattery_DoesNotCancel()
    {
        // Some UPS units briefly report both OL and OB during a transfer; only cancel once OB has cleared.
        var reading = Reading(UpsStatus.OL | UpsStatus.OB);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: true);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }

    [Fact]
    public void Evaluate_ShutdownPending_StillOnBatteryWithoutOl_DoesNotCancel()
    {
        var reading = Reading(UpsStatus.OB);

        var decision = ShutdownPolicy.Evaluate(reading, null, NewSettings(), shutdownPending: true);

        Assert.Equal(ShutdownDecisionKind.None, decision.Kind);
    }
}
