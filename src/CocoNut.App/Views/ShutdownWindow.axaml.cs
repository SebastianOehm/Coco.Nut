using Avalonia.Controls;
using CocoNut.App.ViewModels;

namespace CocoNut.App.Views;

/// <summary>
/// Shutdown countdown window (WinNUT's <c>Shutdown_Gui.vb</c>). The only code-behind behaviour is refusing to
/// close via the window's own close button while the countdown is running, matching WinNUT; every other
/// behaviour lives in <see cref="ShutdownViewModel"/> and <see cref="Services.ShutdownWindowController"/>.
/// </summary>
public partial class ShutdownWindow : Window
{
    public ShutdownWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Only the user is prevented from closing a running countdown (like WinNUT). The controller closes the
        // window programmatically when the shutdown is cancelled or executed, and that must always succeed.
        if (!e.IsProgrammatic && DataContext is ShutdownViewModel { IsRunning: true })
        {
            e.Cancel = true;
        }
    }
}
