using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power.Native;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Platform.Power;

/// <summary>Picks the right <see cref="IPowerActions"/> implementation for the running operating system.</summary>
public static class PowerActionsFactory
{
    public static IPowerActions Create(ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;

        if (OperatingSystem.IsWindows())
        {
            return new WindowsPowerActions(new WindowsPowerNative(), new ProcessRunner(), factory.CreateLogger<WindowsPowerActions>());
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxPowerActions(new ProcessRunner(), new PathExecutableLocator(), factory.CreateLogger<LinuxPowerActions>());
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacPowerActions(new ProcessRunner(), factory.CreateLogger<MacPowerActions>());
        }

        return new UnsupportedPowerActions();
    }
}
