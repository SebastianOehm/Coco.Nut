namespace CocoNut.Core.Shutdown;

/// <summary>Payload of <see cref="ShutdownCoordinator.ShutdownPending"/>.</summary>
public sealed class ShutdownPendingEventArgs : EventArgs
{
    public ShutdownPendingEventArgs(ShutdownReason reason, ShutdownCountdown countdown)
    {
        Reason = reason;
        Countdown = countdown;
    }

    /// <summary>Why the stop procedure started.</summary>
    public ShutdownReason Reason { get; }

    /// <summary>
    /// The running countdown. The UI (WinNUT: <c>Shutdown_Gui</c>) subscribes to its <c>Tick</c>/<c>Completed</c>/
    /// <c>Cancelled</c> events to show progress, and may call <see cref="ShutdownCountdown.TryExtend"/> when
    /// <c>PowerSettings.AllowExtendDelay</c> is set.
    /// </summary>
    public ShutdownCountdown Countdown { get; }
}
