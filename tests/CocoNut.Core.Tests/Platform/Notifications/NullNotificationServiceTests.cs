using CocoNut.Core.Abstractions;
using CocoNut.Platform.Notifications;

namespace CocoNut.Core.Tests.Platform.Notifications;

public class NullNotificationServiceTests
{
    [Theory]
    [InlineData(NotificationKind.Info)]
    [InlineData(NotificationKind.Warning)]
    [InlineData(NotificationKind.Error)]
    public void Notify_DoesNotThrow(NotificationKind kind)
    {
        var service = new NullNotificationService();

        service.Notify("Title", "Message", kind);
    }

    [Fact]
    public void Notify_DefaultsToInfoKind()
    {
        var service = new NullNotificationService();

        service.Notify("Title", "Message");
    }
}
