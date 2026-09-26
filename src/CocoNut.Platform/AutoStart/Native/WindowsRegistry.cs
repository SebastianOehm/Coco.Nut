using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CocoNut.Platform.AutoStart.Native;

/// <summary>
/// Real <see cref="IWindowsRegistry"/>, backed by <see cref="Microsoft.Win32.Registry"/>. Only ever
/// constructed by <c>AutoStartServiceFactory</c> once it has checked <see cref="OperatingSystem.IsWindows"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistry : IWindowsRegistry
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? GetRunValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(valueName) as string;
    }

    public void SetRunValue(string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(valueName, value);
    }

    public void DeleteRunValue(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
