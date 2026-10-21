using System.Globalization;
using CocoNut.App.Controls;
using CocoNut.App.ViewModels;

namespace CocoNut.App.Tests.ViewModels;

public sealed class GaugeViewModelTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    public GaugeViewModelTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _originalCulture;

    private static GaugeViewModel Voltage() => new("Input voltage", "V", "0.0")
    {
        Minimum = 210,
        Maximum = 270,
        Ranges = GaugePresets.InputVoltage(210, 270),
    };

    [Fact]
    public void Digital_value_without_a_reading_is_a_dash()
    {
        var gauge = Voltage();

        Assert.Null(gauge.Value);
        Assert.Equal("—", gauge.DigitalValueText);
        Assert.Null(gauge.StatusBrush);
    }

    [Theory]
    [InlineData("0.0", 231.44, "231.4")]
    [InlineData("0.0", 50, "50.0")]
    [InlineData("0", 32.6, "33")]
    [InlineData("0", 0, "0")]
    [InlineData("0.#", 13.6, "13.6")]
    [InlineData("0.#", 13, "13")]
    public void Digital_value_uses_the_digital_format(string format, double value, string expected)
    {
        var gauge = new GaugeViewModel("Caption", "V", format) { Value = value };

        Assert.Equal(expected, gauge.DigitalValueText);
    }

    [Fact]
    public void Digital_format_defaults_to_an_optional_single_decimal()
    {
        var gauge = new GaugeViewModel("Caption", "V") { Value = 12.34 };

        Assert.Equal("0.#", gauge.DigitalFormat);
        Assert.Equal("12.3", gauge.DigitalValueText);
    }

    [Fact]
    public void Digital_value_uses_the_current_culture()
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        var gauge = new GaugeViewModel("Caption", "V", "0.0") { Value = 231.4 };

        Assert.Equal("231,4", gauge.DigitalValueText);
    }

    [Fact]
    public void Range_text_shows_the_calibrated_scale_and_unit()
    {
        Assert.Equal("210–270 V", Voltage().RangeText);
    }

    [Fact]
    public void Range_text_follows_minimum_and_maximum_changes()
    {
        var gauge = Voltage();
        var changed = new List<string?>();
        gauge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        gauge.Minimum = 200;
        gauge.Maximum = 260.5;

        Assert.Equal("200–260.5 V", gauge.RangeText);
        Assert.Equal(2, changed.Count(n => n == nameof(GaugeViewModel.RangeText)));
    }

    [Fact]
    public void Status_brush_follows_the_value_and_raises_property_changed()
    {
        var gauge = Voltage();
        var changed = new List<string?>();
        gauge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        gauge.Value = 240; // middle band: safe
        Assert.Same(gauge.Ranges[2].Brush, gauge.StatusBrush);

        gauge.Value = 215; // lowest band: danger
        Assert.Same(gauge.Ranges[0].Brush, gauge.StatusBrush);

        gauge.Value = 300; // beyond the scale: the nearest end band
        Assert.Same(gauge.Ranges[4].Brush, gauge.StatusBrush);

        gauge.Value = null;
        Assert.Null(gauge.StatusBrush);

        Assert.Equal(4, changed.Count(n => n == nameof(GaugeViewModel.StatusBrush)));
        Assert.Equal(4, changed.Count(n => n == nameof(GaugeViewModel.DigitalValueText)));
    }

    [Fact]
    public void Replacing_the_ranges_recomputes_the_status_brush()
    {
        var gauge = new GaugeViewModel("Power", "W", "0") { Value = 150 };
        Assert.Null(gauge.StatusBrush);
        var changed = new List<string?>();
        gauge.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        gauge.Ranges = GaugePresets.Power(0, 300);

        Assert.Same(gauge.Ranges[1].Brush, gauge.StatusBrush);
        Assert.Contains(nameof(GaugeViewModel.StatusBrush), changed);
    }
}
