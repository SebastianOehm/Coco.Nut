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
    // We do not query GetPwrCapabilities/HiberFilePresent: it needs a large native struct for a check the
    // WP explicitly marks optional, and getting that struct's layout wrong is a worse failure mode than
    // just reporting "supported" and letting SetSuspendState itself fail if the machine truly has no
    // hibernation file.
    public bool IsHibernateSupported() => true;

    public bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent) =>
        NativeSetSuspendState(hibernate, forceCritical, disableWakeEvent);

    // Plain DllImport: LibraryImport's source-generated marshalling needs AllowUnsafeBlocks even for this
    // simple bool-only signature, which the WP asks us to avoid unless required.
    [DllImport("powrprof.dll", EntryPoint = "SetSuspendState", ExactSpelling = true)]
    private static extern bool NativeSetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
