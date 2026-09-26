using Avalonia.Controls;
using CocoNut.App.Controls;

namespace CocoNut.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // WP-F demo wiring: GaugePresets.* mirror WinNUT's per-gauge coloring for the calibration
        // minimum/maximum set on each Gauge in MainWindow.axaml. The App-shell work package will drive
        // Ranges/Value from the real UpsMonitor/AppSettings instead of these sample readings.
        InputVoltageGauge.Ranges = GaugePresets.InputVoltage(InputVoltageGauge.Minimum, InputVoltageGauge.Maximum);
        OutputVoltageGauge.Ranges = GaugePresets.OutputVoltage(OutputVoltageGauge.Minimum, OutputVoltageGauge.Maximum);
        InputFrequencyGauge.Ranges = GaugePresets.InputFrequency(InputFrequencyGauge.Minimum, InputFrequencyGauge.Maximum);
        BatteryVoltageGauge.Ranges = GaugePresets.BatteryVoltage(BatteryVoltageGauge.Minimum, BatteryVoltageGauge.Maximum);
        LoadGauge.Ranges = GaugePresets.Load(LoadGauge.Minimum, LoadGauge.Maximum);
        PowerGauge.Ranges = GaugePresets.Power(PowerGauge.Minimum, PowerGauge.Maximum);
    }
}
