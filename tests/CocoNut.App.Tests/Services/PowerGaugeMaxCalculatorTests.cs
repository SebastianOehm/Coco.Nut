using CocoNut.App.Services;
using CocoNut.Core.Ups;

namespace CocoNut.App.Tests.Services;

public class PowerGaugeMaxCalculatorTests
{
    private static UpsReading Reading(double? load, double? power) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Status = UpsStatus.OL,
        LoadPercent = load,
        OutputPowerWatts = power,
    };

    [Fact]
    public void Null_reading_returns_the_default_max() =>
        Assert.Equal(PowerGaugeMaxCalculator.DefaultMax, PowerGaugeMaxCalculator.DeriveNominal(null));

    [Theory]
    [InlineData(null, 100.0)]
    [InlineData(0.0, 100.0)]
    [InlineData(50.0, null)]
    public void A_reading_missing_load_or_power_returns_the_default_max(double? load, double? power) =>
        Assert.Equal(PowerGaugeMaxCalculator.DefaultMax, PowerGaugeMaxCalculator.DeriveNominal(Reading(load, power)));

    [Theory]
    [InlineData(50.0, 90.0, 250.0)] // 90 / 0.5 = 180 -> next nice value 250
    [InlineData(20.0, 3000.0, 15_000.0)] // 3000 / 0.2 = 15000, already a nice value
    [InlineData(100.0, 850.0, 1000.0)] // 850 / 1.0 = 850 -> 1000
    public void Nominal_power_rounds_up_to_the_next_nice_value(double load, double power, double expected) =>
        Assert.Equal(expected, PowerGaugeMaxCalculator.DeriveNominal(Reading(load, power)));

    [Fact]
    public void A_nominal_power_above_the_largest_nice_value_rounds_up_to_the_next_thousand()
    {
        // 12345 / 0.1 = 123450, above the largest tabulated nice value (100000).
        var nominal = PowerGaugeMaxCalculator.DeriveNominal(Reading(load: 10, power: 12_345));

        Assert.True(nominal >= 123_450);
        Assert.Equal(0.0, nominal % 1000);
    }

    [Fact]
    public void NextMax_never_shrinks_below_the_current_max()
    {
        var afterHighLoad = PowerGaugeMaxCalculator.NextMax(PowerGaugeMaxCalculator.DefaultMax, Reading(20, 3000));
        Assert.Equal(15_000, afterHighLoad);

        var afterLowLoad = PowerGaugeMaxCalculator.NextMax(afterHighLoad, Reading(50, 100));
        Assert.Equal(15_000, afterLowLoad);
    }

    [Fact]
    public void NextMax_grows_when_a_later_reading_needs_more_headroom()
    {
        var afterFirst = PowerGaugeMaxCalculator.NextMax(PowerGaugeMaxCalculator.DefaultMax, Reading(50, 100));
        var afterSecond = PowerGaugeMaxCalculator.NextMax(afterFirst, Reading(20, 3000));

        Assert.Equal(15_000, afterSecond);
    }
}
