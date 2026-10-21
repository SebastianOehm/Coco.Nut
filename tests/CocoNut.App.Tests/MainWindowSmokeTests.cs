using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CocoNut.App.Controls;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using CocoNut.Core.Updates;
using CocoNut.Localization;

namespace CocoNut.App.Tests;

/// <summary>Headless smoke test: builds a real <see cref="MainWindow"/> with a real <see cref="MainWindowViewModel"/> (fakes underneath) and renders one frame, catching any wiring/binding mistake that only shows up once the view and view model are put together.</summary>
public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_with_its_view_model_renders_a_frame_without_throwing()
    {
        var settingsStore = new InMemorySettingsStore();
        var settingsService = new SettingsService(settingsStore, settingsStore.Settings);
        var vm = new MainWindowViewModel(
            new FakeUpsMonitorEvents(),
            new FakeShutdownEvents(),
            settingsService,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new UpdateChecker(new HttpClient()),
            new ImmediateUiDispatcher(),
            TimeProvider.System,
            requestExit: () => { });

        var window = new MainWindow(settingsService, requestExit: null) { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Gauge_display_shows_the_dials_and_no_digital_tiles()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out _, out _);

        var gauges = window.GetVisualDescendants().OfType<Gauge>().ToList();
        Assert.Equal(6, gauges.Count);
        Assert.All(gauges, g => Assert.True(g.IsEffectivelyVisible));
        Assert.DoesNotContain(VisibleTextBlocks(window), t => t.Text == "231.4");
    }

    [AvaloniaFact]
    public void Digital_display_shows_the_tiles_and_no_dials()
    {
        using var scope = ShowWindow(DisplayStyle.Digital, out var window, out _, out _);

        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Gauge>(), g => g.IsEffectivelyVisible);
        var texts = VisibleTextBlocks(window).Select(t => t.Text).ToList();
        foreach (var expected in new[] { "231.4", "229.8", "50.0", "13.6", "32", "210" })
        {
            Assert.Contains(expected, texts);
        }

        Assert.Contains("210–270 V", texts);
        Assert.Contains("40–60 Hz", texts);
        Assert.Contains(Strings.Main_Gauge_InputVoltage, texts);
    }

    [AvaloniaFact]
    public void Digital_tiles_show_a_dash_without_a_reading()
    {
        using var scope = ShowWindow(DisplayStyle.Digital, out var window, out _, out var monitor);
        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Disconnected);

        Assert.Equal(6, VisibleTextBlocks(window).Count(t => t.Text == GaugeViewModel.NoDataText));
    }

    [AvaloniaFact]
    public void The_view_menu_switches_between_the_dials_and_the_tiles()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out var vm, out _);
        var menuItems = window.GetLogicalDescendants().OfType<MenuItem>().ToList(); // submenu items are only in the visual tree while open
        var viewMenu = Assert.Single(menuItems, m => Equals(m.Header, Strings.Main_Menu_View));
        var gaugesItem = Assert.Single(menuItems, m => Equals(m.Header, Strings.DisplayStyle_Gauges));
        var digitalItem = Assert.Single(menuItems, m => Equals(m.Header, Strings.DisplayStyle_Digital));
        Assert.True(menuItems.IndexOf(viewMenu) < menuItems.IndexOf(Assert.Single(menuItems, m => Equals(m.Header, Strings.Main_Menu_Help))));
        Assert.True(gaugesItem.IsChecked);
        Assert.False(digitalItem.IsChecked);

        digitalItem.IsChecked = true;

        Assert.True(vm.IsDigitalDisplay);
        Assert.False(gaugesItem.IsChecked);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Gauge>(), g => g.IsEffectivelyVisible);

        gaugesItem.IsChecked = true;

        Assert.False(vm.IsDigitalDisplay);
        Assert.False(digitalItem.IsChecked);
        Assert.Equal(6, window.GetVisualDescendants().OfType<Gauge>().Count(g => g.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void The_automatic_reconnection_menu_item_writes_back_to_the_view_model()
    {
        // Regression: MenuItem.IsChecked binds one-way by default, so the check mark never reached the view model.
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out var vm, out _);
        var item = Assert.Single(window.GetLogicalDescendants().OfType<MenuItem>(), m => Equals(m.Header, Strings.Main_Menu_Persist));

        item.IsChecked = true;

        Assert.True(vm.AutoReconnectEnabled);
    }

    private static IEnumerable<TextBlock> VisibleTextBlocks(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible);

    /// <summary>Shows a main window with sample readings in en-US formatting; disposing the result restores the culture and the view model.</summary>
    private static Cleanup ShowWindow(DisplayStyle style, out MainWindow window, out MainWindowViewModel vm, out FakeUpsMonitorEvents monitor)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Strings.Culture = CultureInfo.GetCultureInfo("en");

        monitor = new FakeUpsMonitorEvents();
        var settingsStore = new InMemorySettingsStore();
        settingsStore.Settings.General.DisplayStyle = style;
        var settingsService = new SettingsService(settingsStore, settingsStore.Settings);
        vm = new MainWindowViewModel(
            monitor,
            new FakeShutdownEvents(),
            settingsService,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new UpdateChecker(new HttpClient()),
            new ImmediateUiDispatcher(),
            TimeProvider.System,
            requestExit: () => { });
        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);
        monitor.RaiseReadingUpdated(new UpsReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = UpsStatus.OL,
            BatteryCharge = 92,
            BatteryVoltage = 13.6,
            InputVoltage = 231.4,
            InputFrequency = 50.0,
            OutputVoltage = 229.8,
            LoadPercent = 32,
            OutputPowerWatts = 210,
        });

        window = new MainWindow(settingsService, requestExit: null) { DataContext = vm };
        window.Show();
        window.CaptureRenderedFrame();

        var shownVm = vm;
        return new Cleanup(() =>
        {
            shownVm.Dispose();
            CultureInfo.CurrentCulture = originalCulture;
        });
    }

    private sealed class Cleanup(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
