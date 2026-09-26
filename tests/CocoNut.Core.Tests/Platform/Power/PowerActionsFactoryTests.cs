using CocoNut.Platform.Power;

namespace CocoNut.Core.Tests.Platform.Power;

public class PowerActionsFactoryTests
{
    [Fact]
    public void Create_PicksImplementationMatchingCurrentOperatingSystem()
    {
        var actions = PowerActionsFactory.Create();

        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsPowerActions>(actions);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.IsType<LinuxPowerActions>(actions);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacPowerActions>(actions);
        }
        else
        {
            Assert.IsType<UnsupportedPowerActions>(actions);
        }
    }
}
