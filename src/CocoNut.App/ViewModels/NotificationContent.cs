using Avalonia.Media;
using CocoNut.Core.Abstractions;

namespace CocoNut.App.ViewModels;

/// <summary>Content shown by one <see cref="CocoNut.App.Views.NotificationWindow"/> popup.</summary>
public sealed record NotificationContent(string Title, string Message, NotificationKind Kind)
{
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#2E7D32"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F9A825"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D32F2F"));

    /// <summary>Accent color of the popup's side bar: green/amber/red for info/warning/error.</summary>
    public IBrush AccentBrush => Kind switch
    {
        NotificationKind.Warning => WarningBrush,
        NotificationKind.Error => ErrorBrush,
        _ => InfoBrush,
    };
}
