using System.Globalization;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Formats <see cref="TimeSpan"/> values for the main window's remaining-runtime label and the shutdown
/// countdown window, the two places WinNUT showed a duration (<c>WinNUT.vb</c>'s <c>Lbl_VRTime</c> and
/// <c>Shutdown_Gui</c>'s countdown label). Pure and culture-independent (digits only), so it is unit tested
/// directly without a UI.
/// </summary>
public static class RuntimeFormatter
{
    /// <summary>
    /// Main window remaining-runtime label: always <c>h:mm:ss</c>, or <see cref="Strings.Main_RuntimeUnknown"/>
    /// when <paramref name="runtime"/> is <see langword="null"/>.
    /// </summary>
    public static string FormatRuntime(TimeSpan? runtime) =>
        runtime is { } value ? FormatHms(value) : Strings.Main_RuntimeUnknown;

    /// <summary>Formats <paramref name="value"/> (clamped to non-negative) as <c>h:mm:ss</c>.</summary>
    public static string FormatHms(TimeSpan value)
    {
        value = Clamp(value);
        return string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}");
    }

    /// <summary>
    /// Shutdown countdown label: <c>mm:ss</c> under one hour, <c>h:mm:ss</c> from one hour onward (WinNUT's
    /// <c>Shutdown_Gui</c> countdown label only ever showed minutes:seconds, since its delays are capped low;
    /// Coco.Nut's countdown has no such cap, hence the hour digit once it matters).
    /// </summary>
    public static string FormatCountdown(TimeSpan value)
    {
        value = Clamp(value);
        return value.TotalHours >= 1
            ? FormatHms(value)
            : string.Create(CultureInfo.InvariantCulture, $"{(int)value.TotalMinutes:D2}:{value.Seconds:D2}");
    }

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
