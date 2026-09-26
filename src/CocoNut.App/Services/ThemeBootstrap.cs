using Avalonia;
using Avalonia.Styling;
using CocoNut.Core.Settings;

namespace CocoNut.App.Services;

/// <summary>Applies <see cref="GeneralSettings.Theme"/> to the running application (takes effect immediately).</summary>
public static class ThemeBootstrap
{
    /// <summary>Maps the setting to Avalonia's theme variant; <see cref="AppTheme.System"/> follows the OS.</summary>
    public static ThemeVariant ToThemeVariant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Sets <see cref="Application.RequestedThemeVariant"/>; must be called on the UI thread.</summary>
    public static void Apply(Application application, AppTheme theme)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.RequestedThemeVariant = ToThemeVariant(theme);
    }
}
