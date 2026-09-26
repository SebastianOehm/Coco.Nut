using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CocoNut.App.Views;

/// <summary>About window (WinNUT's <c>About_Gui.vb</c>). No behaviour beyond closing itself.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
