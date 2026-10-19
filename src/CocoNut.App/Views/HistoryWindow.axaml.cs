using Avalonia.Controls;
using Avalonia.Interactivity;
using CocoNut.App.ViewModels;

namespace CocoNut.App.Views;

/// <summary>
/// History window: charts and event list of the recorded UPS history. Code-behind only disposes the view model
/// (which stops its auto-refresh timer) when the window closes; every other behaviour lives in <see cref="HistoryViewModel"/>.
/// </summary>
public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e) => (DataContext as HistoryViewModel)?.Dispose();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
