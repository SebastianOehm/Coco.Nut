using System.Text.Json;

namespace CocoNut.Core.Settings;

/// <summary>Convenience extensions on <see cref="AppSettings"/>.</summary>
public static class AppSettingsExtensions
{
    /// <summary>
    /// Returns a deep, independent copy of <paramref name="settings"/> (via a JSON round-trip) so a settings
    /// dialog can edit a working copy without mutating the live settings until the user confirms.
    /// </summary>
    public static AppSettings Clone(this AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
        return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)
            ?? throw new InvalidOperationException("Cloning AppSettings unexpectedly produced null.");
    }
}
