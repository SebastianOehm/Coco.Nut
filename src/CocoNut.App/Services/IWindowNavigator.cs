using System.Globalization;
using Avalonia.Controls;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>
/// Opens the app's secondary windows. The main window (WP-G) implements <see cref="ShowMainWindow"/> for real;
/// the other four belong to the secondary windows work package (WP-H) and are TODO stubs here that log and show
/// a "not implemented yet" notification instead of a window.
/// </summary>
public interface IWindowNavigator
{
    /// <summary>Opens the Settings window (WP-H: <c>Views/SettingsWindow</c>).</summary>
    Task ShowSettingsAsync();

    /// <summary>Opens the UPS variables window (WP-H: <c>Views/UpsVariablesWindow</c>).</summary>
    Task ShowUpsVariablesAsync();

    /// <summary>Opens the About window (WP-H: <c>Views/AboutWindow</c>).</summary>
    Task ShowAboutAsync();

    /// <summary>Opens the update-available window for <paramref name="result"/> (WP-H: <c>Views/UpdateAvailableWindow</c>).</summary>
    Task ShowUpdateAvailableAsync(UpdateCheckResult result);

    /// <summary>Restores and activates the main window (from the tray icon or a "show" request).</summary>
    void ShowMainWindow();
}

/// <summary>
/// Default <see cref="IWindowNavigator"/>. <see cref="AttachMainWindow"/> must be called once the main window
/// exists (from <c>App.axaml.cs</c>) before <see cref="ShowMainWindow"/> can do anything.
/// </summary>
public sealed class WindowNavigator : IWindowNavigator
{
    private readonly INotificationService _notifications;
    private readonly ILogger<WindowNavigator> _logger;
    private Window? _mainWindow;

    public WindowNavigator(INotificationService notifications, ILogger<WindowNavigator> logger)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Records the main window instance so <see cref="ShowMainWindow"/> can restore/activate it.</summary>
    public void AttachMainWindow(Window mainWindow) => _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

    /// <inheritdoc />
    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    // TODO(WP-H): replace with a real Settings window + SettingsViewModel (docs/PLAN.md: Pref_Gui -> SettingsWindow).
    /// <inheritdoc />
    public Task ShowSettingsAsync() => NotImplementedAsync("Settings window", Strings.Main_Menu_Settings);

    // TODO(WP-H): replace with a real UPS variables window + ViewModel (docs/PLAN.md: List_Var_Gui -> UpsVariablesWindow).
    /// <inheritdoc />
    public Task ShowUpsVariablesAsync() => NotImplementedAsync("UPS variables window", Strings.Main_Menu_UpsVariables);

    // TODO(WP-H): replace with a real About window + ViewModel (docs/PLAN.md: About_Gui -> AboutWindow).
    /// <inheritdoc />
    public Task ShowAboutAsync() => NotImplementedAsync("About window", Strings.Main_Menu_About);

    // TODO(WP-H): replace with a real update-available window + ViewModel (docs/PLAN.md: UpdateAvailableForm -> UpdateAvailableWindow).
    /// <inheritdoc />
    public Task ShowUpdateAvailableAsync(UpdateCheckResult result) => NotImplementedAsync("Update available window", Strings.Update_Title);

    private Task NotImplementedAsync(string windowNameForLog, string localizedWindowName)
    {
        _logger.LogWarning("{Window} is not implemented yet; it belongs to the secondary windows work package.", windowNameForLog);
        _notifications.Notify(
            Strings.Notify_Title_Info,
            string.Format(CultureInfo.CurrentCulture, Strings.Main_FeatureNotImplemented, localizedWindowName),
            NotificationKind.Info);
        return Task.CompletedTask;
    }
}
