using Avalonia.Collections;
using CocoNut.App.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CocoNut.App.ViewModels;

/// <summary>One of the main window's six <see cref="Gauge"/> dials: its live value plus the calibration-derived scale.</summary>
public sealed partial class GaugeViewModel : ObservableObject
{
    public GaugeViewModel(string caption, string unit)
    {
        Caption = caption;
        Unit = unit;
    }

    /// <summary>Static title shown under the dial (e.g. "Input voltage"), fixed at construction time.</summary>
    public string Caption { get; }

    /// <summary>Static unit suffix for the value read-out (e.g. "V"), fixed at construction time.</summary>
    public string Unit { get; }

    /// <summary><see langword="null"/> when there is no current reading (shows "no data" on the dial).</summary>
    [ObservableProperty]
    private double? _value;

    [ObservableProperty]
    private double _minimum;

    [ObservableProperty]
    private double _maximum = 100;

    [ObservableProperty]
    private AvaloniaList<GaugeRange> _ranges = [];
}
