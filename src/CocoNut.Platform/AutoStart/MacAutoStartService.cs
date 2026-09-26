using CocoNut.Core.Abstractions;

namespace CocoNut.Platform.AutoStart;

/// <summary>macOS "start with Windows" equivalent: a per-user LaunchAgent plist.</summary>
internal sealed class MacAutoStartService : IAutoStartService
{
    private const string PlistFileName = "org.nutdotnet.coconut.plist";
    private const string Label = "org.nutdotnet.coconut";

    private readonly string _launchAgentsDirectory;

    /// <param name="launchAgentsDirectory">
    /// The LaunchAgents directory (typically <c>~/Library/LaunchAgents</c>); injectable so tests can point
    /// it at a temp directory.
    /// </param>
    internal MacAutoStartService(string launchAgentsDirectory) => _launchAgentsDirectory = launchAgentsDirectory;

    public bool IsSupported => true;

    private string PlistPath => Path.Combine(_launchAgentsDirectory, PlistFileName);

    public bool IsEnabled() => File.Exists(PlistPath);

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (enabled)
        {
            Directory.CreateDirectory(_launchAgentsDirectory);
            File.WriteAllText(PlistPath, BuildPlist(executablePath));
        }
        else
        {
            // File.Delete is a no-op when the file does not exist, so this stays idempotent.
            File.Delete(PlistPath);
        }
    }

    private static string BuildPlist(string executablePath)
    {
        string[] lines =
        [
            """<?xml version="1.0" encoding="UTF-8"?>""",
            """<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""",
            """<plist version="1.0">""",
            "<dict>",
            "\t<key>Label</key>",
            $"\t<string>{Label}</string>",
            "\t<key>ProgramArguments</key>",
            "\t<array>",
            $"\t\t<string>{EscapeXml(executablePath)}</string>",
            "\t</array>",
            "\t<key>RunAtLoad</key>",
            "\t<true/>",
            "</dict>",
            "</plist>",
        ];
        return string.Join('\n', lines) + "\n";
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}
