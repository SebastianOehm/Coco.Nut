using CocoNut.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Platform.Notifications;

/// <summary>
/// A no-op <see cref="INotificationService"/> that only logs the notification. Useful for unit tests and
/// headless runs; the real desktop app implements its own popup-based service.
/// </summary>
public sealed class NullNotificationService : INotificationService
{
    private readonly ILogger<NullNotificationService> _logger;

    public NullNotificationService(ILogger<NullNotificationService>? logger = null) =>
        _logger = logger ?? NullLogger<NullNotificationService>.Instance;

    public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info) =>
        _logger.Log(MapLevel(kind), "{Title}: {Message}", title, message);

    private static LogLevel MapLevel(NotificationKind kind) => kind switch
    {
        NotificationKind.Warning => LogLevel.Warning,
        NotificationKind.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };
}
