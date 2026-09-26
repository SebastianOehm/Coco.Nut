using CocoNut.Platform.AutoStart;

namespace CocoNut.Core.Tests.Platform.AutoStart;

public class WindowsAutoStartServiceTests
{
    [Fact]
    public void IsEnabled_NoRunValue_ReturnsFalse()
    {
        var service = new WindowsAutoStartService(new FakeWindowsRegistry());

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_True_WritesQuotedExecutablePathUnderCocoNutValueName()
    {
        var registry = new FakeWindowsRegistry();
        var service = new WindowsAutoStartService(registry);

        service.SetEnabled(true, @"C:\Program Files\Coco.Nut\CocoNut.exe");

        Assert.Equal("\"C:\\Program Files\\Coco.Nut\\CocoNut.exe\"", registry.GetRunValue("Coco.Nut"));
        Assert.True(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_False_RemovesRunValue()
    {
        var registry = new FakeWindowsRegistry();
        var service = new WindowsAutoStartService(registry);
        service.SetEnabled(true, @"C:\CocoNut.exe");

        service.SetEnabled(false, @"C:\CocoNut.exe");

        Assert.Null(registry.GetRunValue("Coco.Nut"));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void SetEnabled_False_WhenAlreadyDisabled_IsIdempotent()
    {
        var service = new WindowsAutoStartService(new FakeWindowsRegistry());

        service.SetEnabled(false, @"C:\CocoNut.exe");
        service.SetEnabled(false, @"C:\CocoNut.exe");

        Assert.False(service.IsEnabled());
    }

    [Fact]
    public void IsSupported_IsTrue() => Assert.True(new WindowsAutoStartService(new FakeWindowsRegistry()).IsSupported);
}
