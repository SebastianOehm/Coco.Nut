using System.Text;
using CocoNut.Core.Abstractions;

namespace CocoNut.Platform.AutoStart;

/// <summary>
/// Linux "start with Windows" equivalent: an XDG autostart <c>.desktop</c> file
/// (see https://specifications.freedesktop.org/autostart-spec/ and .../desktop-entry-spec/).
/// </summary>
internal sealed class LinuxAutoStartService : IAutoStartService
{
    private const string DesktopFileName = "coconut.desktop";
    private const string ApplicationName = "Coco.Nut";

    private readonly string _autostartDirectory;

    /// <param name="autostartDirectory">
    /// The XDG autostart directory (typically <c>$XDG_CONFIG_HOME/autostart</c>, falling back to
    /// <c>~/.config/autostart</c>); injectable so tests can point it at a temp directory.
    /// </param>
    internal LinuxAutoStartService(string autostartDirectory) => _autostartDirectory = autostartDirectory;

    public bool IsSupported => true;

    private string DesktopFilePath => Path.Combine(_autostartDirectory, DesktopFileName);

    public bool IsEnabled() => File.Exists(DesktopFilePath);

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            Directory.CreateDirectory(_autostartDirectory);
            File.WriteAllText(DesktopFilePath, BuildDesktopEntry(executablePath));
        }
        else
        {
            // File.Delete is a no-op when the file does not exist, so this stays idempotent.
            File.Delete(DesktopFilePath);
        }
    }

    private static string BuildDesktopEntry(string executablePath)
    {
        string[] lines =
        [
            "[Desktop Entry]",
            "Type=Application",
            $"Name={ApplicationName}",
            $"Exec=\"{EscapeExecValue(executablePath)}\"",
            "X-GNOME-Autostart-enabled=true",
            "Terminal=false",
        ];
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// Escapes a value that will sit inside a double-quoted Exec entry, per the Desktop Entry
    /// Specification: backslash, double quote, backtick and dollar sign must be backslash-escaped.
    /// </summary>
    private static string EscapeExecValue(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or '"' or '`' or '$')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
