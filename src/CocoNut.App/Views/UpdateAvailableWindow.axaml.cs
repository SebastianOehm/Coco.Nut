using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CocoNut.App.Views;

/// <summary>Update-available window (WinNUT's <c>Forms/UpdateAvailableForm.vb</c>). No behaviour beyond closing itself.</summary>
public partial class UpdateAvailableWindow : Window
{
    public UpdateAvailableWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
