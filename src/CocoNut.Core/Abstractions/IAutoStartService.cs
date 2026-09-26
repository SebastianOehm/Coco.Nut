namespace CocoNut.Core.Abstractions;

/// <summary>Registers the application to start with the user session (WinNUT "Start with Windows").</summary>
public interface IAutoStartService
{
    bool IsSupported { get; }

    bool IsEnabled();

    /// <param name="executablePath">Full path of the executable to start.</param>
    void SetEnabled(bool enabled, string executablePath);
}
