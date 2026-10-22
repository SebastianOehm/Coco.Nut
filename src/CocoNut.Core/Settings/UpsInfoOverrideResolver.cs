using CocoNut.Core.History;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Settings;

/// <summary>The product information fields of a UPS that the user may enter by hand when the UPS does not report them.</summary>
public enum UpsInfoField
{
    Manufacturer = 0,
    Model = 1,
}

/// <summary>
/// Reads and writes the manually entered manufacturer/model of <see cref="UpsInfoOverridesSettings"/> and decides
/// which value the main window shows. A value the UPS reports (see <see cref="UpsInfo.IsReported"/>) always wins;
/// a stored override is only used while the UPS does not report the value, and stays stored otherwise.
/// </summary>
public static class UpsInfoOverrideResolver
{
    /// <summary>The entry of <paramref name="key"/> (host case-insensitive, UPS name case-sensitive), or <see langword="null"/>.</summary>
    public static UpsInfoOverride? Find(AppSettings settings, UpsKey key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(key);

        return settings.InfoOverrides.Entries.FirstOrDefault(
            entry => entry is not null && new UpsKey(entry.Host, entry.Port, entry.UpsName).Equals(key));
    }

    /// <summary>The value the user entered for <paramref name="field"/> of the UPS <paramref name="key"/>, or <see langword="null"/> when there is none.</summary>
    public static string? GetOverride(AppSettings settings, UpsKey key, UpsInfoField field) =>
        Find(settings, key) is { } entry ? GetField(entry, field) : null;

    /// <summary>
    /// Stores <paramref name="value"/> (trimmed) as the user-entered <paramref name="field"/> of the UPS <paramref name="key"/>.
    /// Empty or whitespace text clears that field; the entry disappears once both fields are cleared, and is created when needed.
    /// </summary>
    public static void SetOverride(AppSettings settings, UpsKey key, UpsInfoField field, string? value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var entry = Find(settings, key);

        if (entry is null)
        {
            if (trimmed is null)
            {
                return;
            }

            entry = new UpsInfoOverride { Host = key.Host, Port = key.Port, UpsName = key.UpsName };
            settings.InfoOverrides.Entries.Add(entry);
        }

        if (field == UpsInfoField.Manufacturer)
        {
            entry.Manufacturer = trimmed;
        }
        else
        {
            entry.Model = trimmed;
        }

        if (entry.Manufacturer is null && entry.Model is null)
        {
            settings.InfoOverrides.Entries.Remove(entry);
        }
    }

    /// <summary>
    /// The text to display for one field: the reported value when the UPS reports it (never marked as overridden), else the
    /// stored override (marked), else <see langword="null"/> (nothing to show).
    /// </summary>
    public static (string? Text, bool IsOverridden) Resolve(string? reported, string? overrideValue)
    {
        if (UpsInfo.IsReported(reported))
        {
            return (reported!.Trim(), false);
        }

        return string.IsNullOrWhiteSpace(overrideValue) ? (null, false) : (overrideValue.Trim(), true);
    }

    private static string? GetField(UpsInfoOverride entry, UpsInfoField field) =>
        field == UpsInfoField.Manufacturer ? entry.Manufacturer : entry.Model;
}
