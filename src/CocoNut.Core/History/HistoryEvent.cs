namespace CocoNut.Core.History;

/// <summary>
/// What happened, as stored in the events table. The numeric values are persisted: never renumber or reuse one,
/// only append new members.
/// </summary>
public enum HistoryEventKind
{
    /// <summary>The connection to the NUT server was established (also after a reconnect).</summary>
    Connected = 1,
    /// <summary>An established connection broke, or a connection attempt failed for good.</summary>
    ConnectionLost = 2,
    /// <summary>The UPS switched to battery (<c>OB</c> became active).</summary>
    OnBattery = 3,
    /// <summary>Mains power returned (<c>OB</c> cleared).</summary>
    OnLine = 4,
    /// <summary>The UPS reports a low battery (<c>LB</c> became active).</summary>
    LowBattery = 5,
    /// <summary>The server requested a forced shutdown (<c>FSD</c> became active).</summary>
    ForcedShutdown = 6,
    /// <summary>The stop procedure started and its countdown is running.</summary>
    ShutdownPending = 7,
    /// <summary>A pending shutdown was cancelled because power returned.</summary>
    ShutdownCancelled = 8,
    /// <summary>The power action (shutdown, suspend, hibernate) is about to run.</summary>
    ShutdownExecuting = 9,
    /// <summary>The power action failed.</summary>
    ShutdownFailed = 10,
}

/// <summary>
/// One entry of the event history. <paramref name="Detail"/> is invariant, English technical text (reason name,
/// exception message, charge/runtime at that moment); the UI localizes <paramref name="Kind"/> itself.
/// </summary>
public sealed record HistoryEvent(DateTimeOffset Timestamp, UpsKey Ups, HistoryEventKind Kind, string? Detail = null);
