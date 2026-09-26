using System.Globalization;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Applies <see cref="CocoNut.Core.Settings.GeneralSettings.Language"/> before any window is created, per
/// <c>docs/PLAN.md</c>'s localization rules: the UI culture is fixed for the process, a language change taking
/// effect only after a restart (unlike WinNUT, which is Windows Forms and could reload resources on the fly).
/// A pure function of the culture name so it is unit testable without an Avalonia application.
/// </summary>
public static class LocalizationBootstrap
{
    /// <summary>
    /// Sets <see cref="CultureInfo.DefaultThreadCurrentCulture"/>/<see cref="CultureInfo.DefaultThreadCurrentUICulture"/>
    /// (so every thread the app creates from now on, including <see cref="CocoNut.Core.Monitoring.UpsMonitor"/>'s
    /// background loop, inherits it), <see cref="CultureInfo.CurrentUICulture"/> (this thread) and
    /// <see cref="Strings.Culture"/> (the resource manager the whole app reads through).
    /// </summary>
    /// <param name="cultureName">
    /// A culture name such as <c>"de-DE"</c> (<see cref="CocoNut.Core.Settings.GeneralSettings.Language"/>), or
    /// <see langword="null"/>/empty to follow the operating system's installed UI culture.
    /// </param>
    public static void Apply(string? cultureName)
    {
        CultureInfo culture = string.IsNullOrWhiteSpace(cultureName)
            ? CultureInfo.InstalledUICulture
            : CultureInfo.GetCultureInfo(cultureName);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Strings.Culture = culture;
    }
}
