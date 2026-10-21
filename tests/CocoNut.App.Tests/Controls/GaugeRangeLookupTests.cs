using Avalonia.Media;
using CocoNut.App.Controls;

namespace CocoNut.App.Tests.Controls;

public class GaugeRangeLookupTests
{
    private static readonly IBrush Red = Brushes.Red;
    private static readonly IBrush Yellow = Brushes.Yellow;
    private static readonly IBrush Green = Brushes.Green;

    // Bands: [0,10) red, [10,20) yellow, [20,30] green.
    private static readonly GaugeRange[] Bands =
    [
        new() { Start = 0, End = 10, Brush = Red },
        new() { Start = 10, End = 20, Brush = Yellow },
        new() { Start = 20, End = 30, Brush = Green },
    ];

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(10, 1)] // a boundary belongs to the upper band
    [InlineData(15, 1)]
    [InlineData(20, 2)]
    [InlineData(29.9, 2)]
    [InlineData(30, 2)] // the very last End belongs to the last band
    public void A_value_inside_the_scale_finds_its_band(double value, int expectedIndex) =>
        Assert.Same(Bands[expectedIndex], GaugeRangeLookup.Find(Bands, value));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(-1000)]
    public void A_value_below_the_scale_takes_the_first_band(double value) =>
        Assert.Same(Bands[0], GaugeRangeLookup.Find(Bands, value));

    [Theory]
    [InlineData(30.1)]
    [InlineData(1000)]
    [InlineData(double.PositiveInfinity)]
    public void A_value_above_the_scale_takes_the_last_band(double value) =>
        Assert.Same(Bands[^1], GaugeRangeLookup.Find(Bands, value));

    [Fact]
    public void No_value_has_no_band() => Assert.Null(GaugeRangeLookup.Find(Bands, null));

    [Fact]
    public void NaN_has_no_band() => Assert.Null(GaugeRangeLookup.Find(Bands, double.NaN));

    [Fact]
    public void Empty_or_missing_ranges_have_no_band()
    {
        Assert.Null(GaugeRangeLookup.Find([], 5));
        Assert.Null(GaugeRangeLookup.Find(null, 5));
    }

    [Fact]
    public void A_value_in_a_gap_between_bands_has_no_band()
    {
        GaugeRange[] gapped =
        [
            new() { Start = 0, End = 10, Brush = Red },
            new() { Start = 20, End = 30, Brush = Green },
        ];

        Assert.Null(GaugeRangeLookup.Find(gapped, 15));
    }

    [Fact]
    public void A_degenerate_single_band_with_equal_start_and_end_catches_every_value()
    {
        // GaugePresets builds this when the calibrated minimum equals the maximum.
        GaugeRange[] single = [new() { Start = 5, End = 5, Brush = Green }];

        Assert.Same(single[0], GaugeRangeLookup.Find(single, 4));
        Assert.Same(single[0], GaugeRangeLookup.Find(single, 5));
        Assert.Same(single[0], GaugeRangeLookup.Find(single, 6));
    }

    [Fact]
    public void FindBrush_returns_the_brush_of_the_band_or_null()
    {
        var ranges = GaugePresets.Load(0, 99);

        Assert.Same(ranges[0].Brush, GaugeRangeLookup.FindBrush(ranges, 10));
        Assert.Same(ranges[2].Brush, GaugeRangeLookup.FindBrush(ranges, 120));
        Assert.Null(GaugeRangeLookup.FindBrush(ranges, null));
    }

    [Fact]
    public void Presets_find_a_band_for_every_value_across_their_scale()
    {
        var ranges = GaugePresets.InputVoltage(210, 270);

        Assert.Same(ranges[0], GaugeRangeLookup.Find(ranges, 210));
        Assert.Same(ranges[2], GaugeRangeLookup.Find(ranges, 240));
        Assert.Same(ranges[4], GaugeRangeLookup.Find(ranges, 270));
        Assert.Same(ranges[4], GaugeRangeLookup.Find(ranges, 300)); // off the scale reads as the alarming end
        Assert.Same(ranges[0], GaugeRangeLookup.Find(ranges, 0));
    }
}
