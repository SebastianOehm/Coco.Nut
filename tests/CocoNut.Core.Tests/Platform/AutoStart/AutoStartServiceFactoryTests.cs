using CocoNut.Platform.AutoStart;

namespace CocoNut.Core.Tests.Platform.AutoStart;

public class AutoStartServiceFactoryTests
{
    [Fact]
    public void Create_PicksImplementationMatchingCurrentOperatingSystem()
    {
        var service = AutoStartServiceFactory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsAutoStartService>(service);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxAutoStartService>(service);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacAutoStartService>(service);
        }
        else
        {
            Assert.IsType<UnsupportedAutoStartService>(service);
        }
    }
}
