using Avalonia.Controls;
using Avalonia.Input;

namespace CocoNut.App.Views;

/// <summary>
/// A small, borderless, always-on-top popup used by <see cref="CocoNut.App.Services.PopupNotificationService"/>.
/// Positioning/stacking/auto-close is orchestrated by that service; this code-behind only closes the window on
/// a click, per this type's one bit of window plumbing.
/// </summary>
public partial class NotificationWindow : Window
{
    public NotificationWindow() => InitializeComponent();

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e) => Close();
}
