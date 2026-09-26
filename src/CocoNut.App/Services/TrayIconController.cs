using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Ups;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Owns the Avalonia <see cref="TrayIcon"/>: its icon (via <see cref="TrayIconSelector"/>, refreshed on every
/// connection/reading change and theme change), tooltip, and menu (WinNUT's tray <c>ContextMenuStrip</c> in
/// <c>WinNUT.vb</c>). The main window's own <see cref="Window.Icon"/> is kept in sync with the tray icon.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    /// <summary>Windows' notification-area tooltip limit, which WinNUT's <c>NotifyIcon.Text</c> was also subject to.</summary>
    public const int MaxTooltipLength = 63;

    private readonly IUpsMonitorEvents _monitor;
    private readonly ISettingsService _settingsService;
    private readonly IWindowNavigator _navigator;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _requestExit;
    private readonly TrayIcon _trayIcon;
    private Window? _mainWindow;

    public TrayIconController(
        IUpsMonitorEvents monitor,
        ISettingsService settingsService,
        IWindowNavigator navigator,
        IUiDispatcher dispatcher,
        Action requestExit)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));

        var showItem = new NativeMenuItem(Strings.Tray_Menu_Show);
        showItem.Click += (_, _) => _navigator.ShowMainWindow();

        var settingsItem = new NativeMenuItem(Strings.Tray_Menu_Settings);
        settingsItem.Click += (_, _) => _ = _navigator.ShowSettingsAsync();

        var updateItem = new NativeMenuItem(Strings.Tray_Menu_CheckForUpdate);
        updateItem.Click += (_, _) => CheckForUpdatesRequested?.Invoke(this, EventArgs.Empty);

        var aboutItem = new NativeMenuItem(Strings.Tray_Menu_About);
        aboutItem.Click += (_, _) => _ = _navigator.ShowAboutAsync();

        var exitItem = new NativeMenuItem(Strings.Tray_Menu_Exit);
        exitItem.Click += (_, _) => _requestExit();

        _trayIcon = new TrayIcon
        {
            Menu = new NativeMenu { showItem, settingsItem, updateItem, aboutItem, new NativeMenuItemSeparator(), exitItem },
        };
        _trayIcon.Clicked += (_, _) => _navigator.ShowMainWindow();

        _monitor.StateChanged += (_, _) => _dispatcher.Post(RefreshIcon);
        _monitor.ReadingUpdated += (_, _) => _dispatcher.Post(RefreshIcon);
        _settingsService.SettingsChanged += (_, _) => _dispatcher.Post(RefreshIcon);
    }

    /// <summary>Raised when the tray menu's "Check for updates" item is clicked.</summary>
    public event EventHandler? CheckForUpdatesRequested;

    /// <summary>
    /// Registers the tray icon with <paramref name="application"/> and keeps <paramref name="mainWindow"/>'s
    /// own <see cref="Window.Icon"/> in sync with it from now on.
    /// </summary>
    public void Attach(Application application, Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        TrayIcon.SetIcons(application, new TrayIcons { _trayIcon });
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        var state = _monitor.State;
        var reading = _monitor.LastReading;
        var status = reading?.Status ?? UpsStatus.None;
        var darkTheme = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

        var asset = TrayIconSelector.SelectIconAsset(
            connected: state == MonitorState.Connected,
            reconnecting: state == MonitorState.Reconnecting,
            status,
            reading?.BatteryCharge,
            darkTheme);
        var icon = TrayIconSelector.LoadIcon(asset);

        _trayIcon.Icon = icon;
        if (_mainWindow is not null)
        {
            _mainWindow.Icon = icon;
        }

        var statusText = ConnectionStatusTextBuilder.Build(state, null);
        var upsName = _settingsService.Current.Connection.UpsName;
        _trayIcon.ToolTipText = BuildTooltip(upsName, statusText);
    }

    /// <summary>
    /// Formats <see cref="Strings.Tray_Tooltip"/> from <paramref name="upsName"/>/<paramref name="statusText"/>
    /// and truncates it to <see cref="MaxTooltipLength"/> characters if needed.
    /// </summary>
    public static string BuildTooltip(string upsName, string statusText)
    {
        var tooltip = string.Format(CultureInfo.CurrentCulture, Strings.Tray_Tooltip, upsName, statusText);
        return tooltip.Length <= MaxTooltipLength ? tooltip : tooltip[..MaxTooltipLength];
    }

    /// <inheritdoc />
    public void Dispose() => _trayIcon.Dispose();
}
