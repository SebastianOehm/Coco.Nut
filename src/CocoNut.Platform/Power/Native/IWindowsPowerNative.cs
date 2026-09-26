namespace CocoNut.Platform.Power.Native;

/// <summary>
/// Seam around the Windows-only power APIs (<c>powrprof.dll</c>) so <c>WindowsPowerActions</c> stays
/// testable on every OS. The real implementation is only ever constructed on Windows.
/// </summary>
internal interface IWindowsPowerNative
{
    /// <summary>Whether hibernation is available on this machine (e.g. a hibernation file is present).</summary>
    bool IsHibernateSupported();

    /// <summary>Calls <c>SetSuspendState</c>. Returns <see langword="false"/> when Windows reports failure.</summary>
    bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
