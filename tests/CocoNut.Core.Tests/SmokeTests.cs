using CocoNut.Localization;

namespace CocoNut.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Strings_AreAccessible() => Assert.Equal("Coco.Nut", Strings.AppName);
}
