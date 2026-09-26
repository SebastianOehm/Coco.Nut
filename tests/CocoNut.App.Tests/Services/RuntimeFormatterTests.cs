using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Localization;

namespace CocoNut.App.Tests.Services;

public class RuntimeFormatterTests
{
    public RuntimeFormatterTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    [Fact]
    public void FormatRuntime_of_null_returns_the_unknown_string() =>
        Assert.Equal(Strings.Main_RuntimeUnknown, RuntimeFormatter.FormatRuntime(null));

    [Theory]
    [InlineData(0, 5, 30, "0:05:30")]
    [InlineData(2, 3, 9, "2:03:09")]
    [InlineData(0, 0, 5, "0:00:05")]
    public void FormatRuntime_always_uses_hms(int hours, int minutes, int seconds, string expected)
    {
        var value = new TimeSpan(hours, minutes, seconds);

        Assert.Equal(expected, RuntimeFormatter.FormatRuntime(value));
    }

    [Fact]
    public void FormatHms_clamps_a_negative_span_to_zero() =>
        Assert.Equal("0:00:00", RuntimeFormatter.FormatHms(TimeSpan.FromSeconds(-5)));

    [Theory]
    [InlineData(0, 1, 30, "01:30")]
    [InlineData(0, 59, 59, "59:59")]
    public void FormatCountdown_under_one_hour_uses_mmss(int hours, int minutes, int seconds, string expected)
    {
        var value = new TimeSpan(hours, minutes, seconds);

        Assert.Equal(expected, RuntimeFormatter.FormatCountdown(value));
    }

    [Theory]
    [InlineData(1, 0, 0, "1:00:00")]
    [InlineData(2, 5, 9, "2:05:09")]
    public void FormatCountdown_at_or_above_one_hour_uses_hmmss(int hours, int minutes, int seconds, string expected)
    {
        var value = new TimeSpan(hours, minutes, seconds);

        Assert.Equal(expected, RuntimeFormatter.FormatCountdown(value));
    }
}
