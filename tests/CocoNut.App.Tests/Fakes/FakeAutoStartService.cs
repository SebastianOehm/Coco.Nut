using CocoNut.Core.Abstractions;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Drives <see cref="IAutoStartService"/> consumers without touching the real OS autostart mechanism.</summary>
public sealed class FakeAutoStartService : IAutoStartService
{
    public bool IsSupported { get; set; } = true;

    private bool _enabled;

    public int SetEnabledCallCount { get; private set; }

    public bool? LastEnabledValue { get; private set; }

    public string? LastExecutablePath { get; private set; }

    public bool IsEnabled() => _enabled;

    public void SetEnabled(bool enabled, string executablePath)
    {
        SetEnabledCallCount++;
        LastEnabledValue = enabled;
        LastExecutablePath = executablePath;
        _enabled = enabled;
    }
}
