using CocoNut.Core.Abstractions;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Records every <see cref="Notify"/> call instead of showing a popup.</summary>
public sealed class FakeNotificationService : INotificationService
{
    public List<(string Title, string Message, NotificationKind Kind)> Notifications { get; } = [];

    public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info) =>
        Notifications.Add((title, message, kind));
}
