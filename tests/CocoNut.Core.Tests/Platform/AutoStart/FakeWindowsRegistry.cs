using CocoNut.Platform.AutoStart.Native;

namespace CocoNut.Core.Tests.Platform.AutoStart;

/// <summary>An in-memory stand-in for the real <c>HKCU\...\Run</c> key.</summary>
internal sealed class FakeWindowsRegistry : IWindowsRegistry
{
    private readonly Dictionary<string, string> _values = [];

    public string? GetRunValue(string valueName) => _values.GetValueOrDefault(valueName);

    public void SetRunValue(string valueName, string value) => _values[valueName] = value;

    public void DeleteRunValue(string valueName) => _values.Remove(valueName);
}
