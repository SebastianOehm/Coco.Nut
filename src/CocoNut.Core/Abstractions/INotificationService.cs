namespace CocoNut.Core.Abstractions;

public enum NotificationKind
{
    Info,
    Warning,
    Error,
}

/// <summary>Shows a desktop notification (WinNUT used Windows toasts). Texts are already localized.</summary>
public interface INotificationService
{
    void Notify(string title, string message, NotificationKind kind = NotificationKind.Info);
}
