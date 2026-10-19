using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CocoNut.Platform.Power.Native;

/// <summary>
/// Real <see cref="IWindowsPowerNative"/>, P/Invoking <c>powrprof.dll</c>. Only ever constructed by
/// <c>PowerActionsFactory</c> once it has checked <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerNative : IWindowsPowerNative
{
    // IsPwrHibernateAllowed is false when hibernation is disabled or there is no hibernation file. If the call
    // itself cannot be made, report "supported" and let SetSuspendState fail instead.
    public bool IsHibernateSupported()
    {
        try
        {
            return NativeIsPwrHibernateAllowed();
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    public bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent) =>
        NativeSetSuspendState(hibernate, forceCritical, disableWakeEvent);

    // Plain DllImport: LibraryImport's source-generated marshalling needs AllowUnsafeBlocks even for this
    // simple bool-only signature, which the WP asks us to avoid unless required.
    // SetSuspendState takes and returns BOOLEAN (1 byte), not BOOL (4 bytes, the default bool marshalling).
    [DllImport("powrprof.dll", EntryPoint = "SetSuspendState", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool NativeSetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical,
        [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);

    [DllImport("powrprof.dll", EntryPoint = "IsPwrHibernateAllowed", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool NativeIsPwrHibernateAllowed();
}
