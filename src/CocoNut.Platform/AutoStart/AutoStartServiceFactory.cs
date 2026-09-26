using CocoNut.Core.Abstractions;
using CocoNut.Platform.AutoStart.Native;

namespace CocoNut.Platform.AutoStart;

/// <summary>Picks the right <see cref="IAutoStartService"/> implementation for the running operating system.</summary>
public static class AutoStartServiceFactory
{
    public static IAutoStartService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsAutoStartService(new WindowsRegistry());
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxAutoStartService(GetLinuxAutostartDirectory());
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacAutoStartService(GetMacLaunchAgentsDirectory());
        }

        return new UnsupportedAutoStartService();
    }

    private static string GetLinuxAutostartDirectory()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdgConfigHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdgConfigHome;
        return Path.Combine(configHome, "autostart");
    }

    private static string GetMacLaunchAgentsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
}
