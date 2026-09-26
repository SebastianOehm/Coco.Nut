using Avalonia;
using Avalonia.Controls;
using CocoNut.App.Services;

namespace CocoNut.App.Views;

/// <summary>
/// Main window code-behind: only the close-to-tray/minimize-to-tray window plumbing from
/// <c>AppSettings.General</c> (WinNUT's <c>WinNUT.vb</c> had the same close/minimize-to-tray behaviour); every
/// other behaviour lives in <see cref="CocoNut.App.ViewModels.MainWindowViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly ISettingsService? _settingsService;
    private readonly Action? _requestExit;

    /// <summary>Parameterless constructor for the XAML loader/previewer only; behaves as if closing always exits.</summary>
    public MainWindow() : this(null, null)
    {
    }

    public MainWindow(ISettingsService? settingsService, Action? requestExit)
    {
        _settingsService = settingsService;
        _requestExit = requestExit;
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        var closeToTray = _settingsService?.Current.General.CloseToTray ?? false;
        if (closeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        // Not closing to tray: route through the app's single exit path (menu Exit/tray Exit use the same one)
        // instead of letting this Close() proceed directly, so AppHost disposal always happens the same way.
        e.Cancel = true;
        _requestExit?.Invoke();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != WindowStateProperty || WindowState != WindowState.Minimized)
        {
            return;
        }

        if (_settingsService?.Current.General.MinimizeToTray ?? false)
        {
            Hide();
        }
    }
}
