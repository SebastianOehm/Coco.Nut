using System.Globalization;
using Avalonia.Collections;
using Avalonia.Media;
using CocoNut.App.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CocoNut.App.ViewModels;

/// <summary>
/// One of the main window's six values: its live value plus the calibration-derived scale. Shown either as a
/// <see cref="Gauge"/> dial or as a digital tile (<see cref="DigitalValueText"/>, <see cref="RangeText"/>,
/// <see cref="StatusBrush"/>).
/// </summary>
public sealed partial class GaugeViewModel : ObservableObject
{
    /// <summary>Digital read-out of a missing value, same as the dial's.</summary>
    public const string NoDataText = "—";

    public GaugeViewModel(string caption, string unit, string digitalFormat = "0.#")
    {
        Caption = caption;
        Unit = unit;
        DigitalFormat = digitalFormat;
    }

    /// <summary>Static title shown under the dial (e.g. "Input voltage"), fixed at construction time.</summary>
    public string Caption { get; }

    /// <summary>Static unit suffix for the value read-out (e.g. "V"), fixed at construction time.</summary>
    public string Unit { get; }

    /// <summary>Numeric format of the digital read-out (e.g. "0.0"), fixed at construction time; the dial keeps its own format.</summary>
    public string DigitalFormat { get; }

    /// <summary><see langword="null"/> when there is no current reading (shows "no data" on the dial).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DigitalValueText))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private double? _value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private double _minimum;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private double _maximum = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private AvaloniaList<GaugeRange> _ranges = [];

    /// <summary>The value formatted with <see cref="DigitalFormat"/> in the current culture, or <see cref="NoDataText"/> when there is no reading.</summary>
    public string DigitalValueText => Value is { } value ? value.ToString(DigitalFormat, CultureInfo.CurrentCulture) : NoDataText;

    /// <summary>The calibrated scale as text, e.g. "210–270 V".</summary>
    public string RangeText => string.Format(
        CultureInfo.CurrentCulture, "{0}–{1} {2}",
        Minimum.ToString("0.#", CultureInfo.CurrentCulture), Maximum.ToString("0.#", CultureInfo.CurrentCulture), Unit);

    /// <summary>Brush of the <see cref="Ranges"/> band the current value is in (see <see cref="GaugeRangeLookup"/>); <see langword="null"/> without a reading or ranges.</summary>
    public IBrush? StatusBrush => GaugeRangeLookup.FindBrush(Ranges, Value);
}
