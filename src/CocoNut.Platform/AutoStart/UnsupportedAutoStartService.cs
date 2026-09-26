using CocoNut.Core.Abstractions;

namespace CocoNut.Platform.AutoStart;

/// <summary>Fallback for an operating system we have no autostart integration for (e.g. FreeBSD).</summary>
internal sealed class UnsupportedAutoStartService : IAutoStartService
{
    public bool IsSupported => false;

    public bool IsEnabled() => false;

    public void SetEnabled(bool enabled, string executablePath) =>
        throw new PlatformNotSupportedException("Start-with-OS is not supported on this operating system.");
}
