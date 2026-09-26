using CocoNut.Core.Ups;

namespace CocoNut.Core.Monitoring;

/// <summary>
/// Payload of <see cref="UpsMonitor.StatusChanged"/>, raised only when <see cref="Current"/> differs from
/// <see cref="Previous"/>. WinNUT's <c>UPS_Device.StatusesChanged</c> only exposed the newly-active flags
/// (<c>(oldStatusBitmask Xor newStatus) And newStatus</c>); this also exposes the flags that were newly cleared.
/// </summary>
public sealed class UpsStatusChangedEventArgs : EventArgs
{
    public UpsStatusChangedEventArgs(UpsStatus previous, UpsStatus current, UpsStatus newlyActive, UpsStatus newlyCleared)
    {
        Previous = previous;
        Current = current;
        NewlyActive = newlyActive;
        NewlyCleared = newlyCleared;
    }

    public UpsStatus Previous { get; }

    public UpsStatus Current { get; }

    /// <summary>Flags present in <see cref="Current"/> but not in <see cref="Previous"/>.</summary>
    public UpsStatus NewlyActive { get; }

    /// <summary>Flags present in <see cref="Previous"/> but not in <see cref="Current"/>.</summary>
    public UpsStatus NewlyCleared { get; }
}
