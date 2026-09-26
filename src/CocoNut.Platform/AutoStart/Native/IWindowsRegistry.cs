namespace CocoNut.Platform.AutoStart.Native;

/// <summary>
/// Seam around the <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> key so
/// <c>WindowsAutoStartService</c> stays testable on every OS. The real implementation is only ever
/// constructed on Windows.
/// </summary>
internal interface IWindowsRegistry
{
    /// <summary>The current string value, or <see langword="null"/> when the value (or the key) is absent.</summary>
    string? GetRunValue(string valueName);

    void SetRunValue(string valueName, string value);

    /// <summary>No-op when the value (or the key) does not exist.</summary>
    void DeleteRunValue(string valueName);
}
