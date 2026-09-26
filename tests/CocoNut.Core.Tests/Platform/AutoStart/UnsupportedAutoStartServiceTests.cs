using CocoNut.Platform.AutoStart;

namespace CocoNut.Core.Tests.Platform.AutoStart;

public class UnsupportedAutoStartServiceTests
{
    [Fact]
    public void IsSupported_IsFalse() => Assert.False(new UnsupportedAutoStartService().IsSupported);

    [Fact]
    public void IsEnabled_IsFalse() => Assert.False(new UnsupportedAutoStartService().IsEnabled());

    [Fact]
    public void SetEnabled_Throws() =>
        Assert.Throws<PlatformNotSupportedException>(() => new UnsupportedAutoStartService().SetEnabled(true, "/anywhere"));
}
