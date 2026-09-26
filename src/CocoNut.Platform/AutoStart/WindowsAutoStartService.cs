using CocoNut.Core.Abstractions;
using CocoNut.Platform.AutoStart.Native;

namespace CocoNut.Platform.AutoStart;

/// <summary>
/// Windows "start with Windows": a value in <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.
/// Matches WinNUT's <c>Pref_Gui.vb</c> (<c>My.Computer.Registry...Run</c>), keyed under the app name instead
/// of <c>Application.ProductName</c>.
/// </summary>
internal sealed class WindowsAutoStartService : IAutoStartService
{
    private const string ValueName = "Coco.Nut";

    private readonly IWindowsRegistry _registry;

    internal WindowsAutoStartService(IWindowsRegistry registry) => _registry = registry;

    public bool IsSupported => true;

    public bool IsEnabled() => _registry.GetRunValue(ValueName) is not null;

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            _registry.SetRunValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            _registry.DeleteRunValue(ValueName);
        }
    }
}
