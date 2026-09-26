using Avalonia.Controls;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>Opens the app's secondary windows: Settings, UPS variables, About and the update-available notice.</summary>
public interface IWindowNavigator
{
    /// <summary>Opens the Settings window (<c>Views/SettingsWindow</c>), or activates it if already open.</summary>
    Task ShowSettingsAsync();

    /// <summary>Opens the UPS variables window (<c>Views/UpsVariablesWindow</c>), or activates it if already open.</summary>
    Task ShowUpsVariablesAsync();

    /// <summary>Opens the About window (<c>Views/AboutWindow</c>), or activates it if already open.</summary>
    Task ShowAboutAsync();

    /// <summary>
    /// Opens the update-available window (<c>Views/UpdateAvailableWindow</c>) for <paramref name="result"/>, or
    /// activates it if already open (the already-open instance keeps showing whichever result it was opened with).
    /// </summary>
    Task ShowUpdateAvailableAsync(UpdateCheckResult result);

    /// <summary>Restores and activates the main window (from the tray icon or a "show" request).</summary>
    void ShowMainWindow();
}

/// <summary>
/// Default <see cref="IWindowNavigator"/>. Each secondary window is singleton-per-open: calling its "show" method
/// again while it is still open activates the existing instance instead of creating a second one. A window is
/// owned by (and centered on) the main window when that one is currently visible, and otherwise shown on its own,
/// centered on the screen. <see cref="AttachMainWindow"/> must be called once the main window exists (from
/// <c>App.axaml.cs</c>) before any of this can do anything useful.
/// </summary>
public sealed class WindowNavigator : IWindowNavigator
{
    private readonly IServiceProvider _services;
    private readonly ILogger<WindowNavigator> _logger;
    private Window? _mainWindow;

    private SettingsWindow? _settingsWindow;
    private UpsVariablesWindow? _upsVariablesWindow;
    private AboutWindow? _aboutWindow;
    private UpdateAvailableWindow? _updateAvailableWindow;

    /// <param name="services">
    /// Resolves a fresh view model for each window the first time it is opened (view models are registered
    /// transient; the navigator itself is a long-lived singleton and must not hold one past that window's close).
    /// </param>
    public WindowNavigator(IServiceProvider services, ILogger<WindowNavigator> logger)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Records the main window instance so <see cref="ShowMainWindow"/> and window ownership can use it.</summary>
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

    /// <inheritdoc />
    public Task ShowSettingsAsync()
    {
        if (Activate(_settingsWindow))
        {
            return Task.CompletedTask;
        }

        var viewModel = _services.GetRequiredService<SettingsViewModel>();
        var window = new SettingsWindow { DataContext = viewModel };
        _settingsWindow = window;
        window.Closed += (_, _) => _settingsWindow = null;

        _logger.LogDebug("Opening the Settings window.");
        Show(window);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowUpsVariablesAsync()
    {
        if (Activate(_upsVariablesWindow))
        {
            return Task.CompletedTask;
        }

        var viewModel = _services.GetRequiredService<UpsVariablesViewModel>();
        var window = new UpsVariablesWindow { DataContext = viewModel };
        _upsVariablesWindow = window;
        window.Closed += (_, _) => _upsVariablesWindow = null;

        _logger.LogDebug("Opening the UPS variables window.");
        Show(window);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowAboutAsync()
    {
        if (Activate(_aboutWindow))
        {
            return Task.CompletedTask;
        }

        var viewModel = _services.GetRequiredService<AboutViewModel>();
        var window = new AboutWindow { DataContext = viewModel };
        _aboutWindow = window;
        window.Closed += (_, _) => _aboutWindow = null;

        _logger.LogDebug("Opening the About window.");
        Show(window);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowUpdateAvailableAsync(UpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (Activate(_updateAvailableWindow))
        {
            return Task.CompletedTask;
        }

        var shellLauncher = _services.GetRequiredService<IShellLauncher>();
        var currentVersion = typeof(WindowNavigator).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var viewModel = new UpdateAvailableViewModel(result, currentVersion, shellLauncher);
        var window = new UpdateAvailableWindow { DataContext = viewModel };
        _updateAvailableWindow = window;
        window.Closed += (_, _) => _updateAvailableWindow = null;

        _logger.LogDebug("Opening the update-available window.");
        Show(window);
        return Task.CompletedTask;
    }

    /// <summary>If <paramref name="window"/> is already open, restores and activates it. Returns whether it was.</summary>
    private static bool Activate(Window? window)
    {
        if (window is null)
        {
            return false;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        return true;
    }

    /// <summary>Shows <paramref name="window"/> owned by the main window when it is visible, or on its own otherwise.</summary>
    private void Show(Window window)
    {
        if (_mainWindow is { IsVisible: true })
        {
            window.Show(_mainWindow);
        }
        else
        {
            window.Show();
        }
    }
}
