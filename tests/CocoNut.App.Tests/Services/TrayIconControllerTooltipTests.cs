using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Localization;

namespace CocoNut.App.Tests.Services;

public class TrayIconControllerTooltipTests
{
    public TrayIconControllerTooltipTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void A_short_ups_name_and_status_are_not_truncated()
    {
        var tooltip = TrayIconController.BuildTooltip("ups", "Connected");

        Assert.Equal("ups – Connected", tooltip);
        Assert.True(tooltip.Length <= TrayIconController.MaxTooltipLength);
    }

    [Fact]
    public void A_long_ups_name_and_status_are_truncated_to_the_windows_tooltip_limit()
    {
        var longName = new string('x', 80);

        var tooltip = TrayIconController.BuildTooltip(longName, "Reconnecting");

        Assert.Equal(TrayIconController.MaxTooltipLength, tooltip.Length);
    }

    [Fact]
    public void The_tooltip_limit_matches_windows_notifyicon_text()
    {
        Assert.Equal(63, TrayIconController.MaxTooltipLength);
    }
}
