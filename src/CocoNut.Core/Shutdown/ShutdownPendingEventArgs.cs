namespace CocoNut.Core.Shutdown;

/// <summary>Payload of <see cref="ShutdownCoordinator.ShutdownPending"/>.</summary>
public sealed class ShutdownPendingEventArgs : EventArgs
{
    public ShutdownPendingEventArgs(ShutdownReason reason, ShutdownCountdown countdown, string? ruleName = null)
    {
        Reason = reason;
        Countdown = countdown;
        RuleName = ruleName;
    }

    /// <summary>Why the stop procedure started.</summary>
    public ShutdownReason Reason { get; }

    /// <summary>The name of the rule that started the stop; shown for <see cref="ShutdownReason.CustomRule"/>.</summary>
    public string? RuleName { get; }

    /// <summary>
    /// The running countdown. The UI (WinNUT: <c>Shutdown_Gui</c>) subscribes to its <c>Tick</c>/<c>Completed</c>/
    /// <c>Cancelled</c> events to show progress, and may call <see cref="ShutdownCountdown.TryExtend"/> when
    /// <c>PowerSettings.AllowExtendDelay</c> is set.
    /// </summary>
    public ShutdownCountdown Countdown { get; }
}
