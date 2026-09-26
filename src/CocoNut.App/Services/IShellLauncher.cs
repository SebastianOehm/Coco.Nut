using System.Diagnostics;

namespace CocoNut.App.Services;

/// <summary>
/// Opens a URL or a local file with the operating system's default handler (a browser, a text editor, ...).
/// Replaces WinNUT's bare <c>Process.Start(path)</c> calls (<c>About_Gui</c>'s GitHub link, <c>Pref_Gui</c>'s
/// "view log" button, <c>UpdateAvailableForm</c>'s release page button) with something that also works reliably
/// on Linux, where a path/URL is not itself executable the way <c>ShellExecute</c> makes it on Windows.
/// </summary>
public interface IShellLauncher
{
    /// <summary>Opens <paramref name="url"/> in the user's default web browser.</summary>
    void OpenUrl(string url);

    /// <summary>Opens the file at <paramref name="path"/> with its associated application.</summary>
    void OpenFile(string path);
}

/// <summary>Default <see cref="IShellLauncher"/>, backed by <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
public sealed class ShellLauncher : IShellLauncher
{
    /// <inheritdoc />
    public void OpenUrl(string url) => Open(url);

    /// <inheritdoc />
    public void OpenFile(string path) => Open(path);

    /// <summary>
    /// Tries <see cref="ProcessStartInfo.UseShellExecute"/> first (works for both files and URLs on Windows and
    /// macOS, and for most Linux desktops too); falls back to the platform's own opener command when that fails,
    /// since <c>UseShellExecute</c> support for arbitrary URIs varies across Linux distributions/sandboxes.
    /// </summary>
    private static void Open(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (OperatingSystem.IsLinux())
            {
                using var process = Process.Start(new ProcessStartInfo("xdg-open", [target]) { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var process = Process.Start(new ProcessStartInfo("open", [target]) { UseShellExecute = false });
            }
            else
            {
                throw;
            }
        }
    }
}
