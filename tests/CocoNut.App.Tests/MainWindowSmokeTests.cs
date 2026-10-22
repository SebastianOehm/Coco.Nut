using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CocoNut.App.Controls;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.History;
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

    [AvaloniaFact]
    public void A_manually_entered_model_shows_the_override_indicator_and_the_reported_manufacturer_does_not()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out _, out _, new UpsInfo("Acme", "Unknown", "SN1", "1.0"),
            settings => UpsInfoOverrideResolver.SetOverride(settings, new UpsKey("nutserver", 3493, "ups"), UpsInfoField.Model, "X-1000"));

        var texts = VisibleTextBlocks(window).Select(t => t.Text).ToList();
        Assert.Contains("Acme", texts);
        Assert.Contains("X-1000", texts);
        Assert.DoesNotContain(Strings.Common_Unavailable, texts.Take(2));

        var indicator = Assert.Single(VisibleButtons(window, Strings.Main_InfoOverride_Tooltip));
        Assert.Equal(Strings.Main_InfoOverride_Tooltip, ToolTip.GetTip(indicator));
        Assert.True(indicator.IsEffectivelyVisible);
        Assert.NotNull(indicator.Command);
        Assert.Empty(VisibleButtons(window, Strings.Main_InfoOverride_Edit));

        // The marker differs by shape (person with pencil), not only by color, and the manual value is italic.
        var icon = Assert.Single(VisibleIcons(indicator));
        Assert.Contains("userEditIcon", icon.Classes);
        Assert.Equal(FontStyle.Italic, VisibleTextBlocks(window).First(t => t.Text == "X-1000").FontStyle);
        Assert.Equal(FontStyle.Normal, VisibleTextBlocks(window).First(t => t.Text == "Acme").FontStyle);
    }

    [AvaloniaFact]
    public void An_undetected_manufacturer_and_model_show_unavailable_with_an_edit_button_each()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out _, out _, new UpsInfo("Unknown", "Unknown", "SN1", "1.0"));

        Assert.Equal(2, VisibleButtons(window, Strings.Main_InfoOverride_Edit).Count);
        Assert.Empty(VisibleButtons(window, Strings.Main_InfoOverride_Tooltip));
        Assert.Contains(Strings.Common_Unavailable, VisibleTextBlocks(window).Select(t => t.Text));
        Assert.All(VisibleButtons(window, Strings.Main_InfoOverride_Edit), b =>
        {
            Assert.Contains("plainIcon", Assert.Single(VisibleIcons(b)).Classes);
        });
        Assert.All(VisibleTextBlocks(window).Where(t => t.Text == Strings.Common_Unavailable), t => Assert.Equal(FontStyle.Normal, t.FontStyle));
    }

    [AvaloniaFact]
    public void A_detected_manufacturer_and_model_have_neither_edit_button_nor_indicator()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out _, out _, new UpsInfo("Acme", "X-1000", "SN1", "1.0"));

        Assert.Empty(VisibleButtons(window, Strings.Main_InfoOverride_Edit));
        Assert.Empty(VisibleButtons(window, Strings.Main_InfoOverride_Tooltip));
    }

    [AvaloniaFact]
    public void The_inline_editor_saves_with_enter_and_cancels_with_escape()
    {
        using var scope = ShowWindow(DisplayStyle.Gauges, out var window, out var vm, out _, new UpsInfo("Unknown", "Unknown", "SN1", "1.0"));

        VisibleButtons(window, Strings.Main_InfoOverride_Edit)[0].Command!.Execute(null); // manufacturer
        window.CaptureRenderedFrame();
        var editor = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
        Assert.Equal(UpsInfoFieldViewModel.MaxLength, editor.MaxLength);
        Assert.Equal(Strings.Main_InfoOverride_Watermark, editor.Watermark);
        Assert.Single(VisibleButtons(window, Strings.Common_Save));
        Assert.Single(VisibleButtons(window, Strings.Common_Cancel));
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.IsFocused, "the editor takes the focus when it opens");

        editor.Text = "Acme Corp";
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(vm.Manufacturer.IsEditing);
        Assert.Equal(Strings.Common_Unavailable, vm.Manufacturer.Text);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);

        VisibleButtons(window, Strings.Main_InfoOverride_Edit)[0].Command!.Execute(null);
        window.CaptureRenderedFrame();
        editor = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible);
        editor.Focus();
        editor.Text = "Acme Corp";
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.CaptureRenderedFrame();

        Assert.False(vm.Manufacturer.IsEditing);
        Assert.Equal("Acme Corp", vm.Manufacturer.Text);
        Assert.True(vm.Manufacturer.IsOverridden);
        Assert.Contains("Acme Corp", VisibleTextBlocks(window).Select(t => t.Text));
        Assert.Single(VisibleButtons(window, Strings.Main_InfoOverride_Tooltip));
    }

    private static List<PathIcon> VisibleIcons(Button button) =>
        button.GetVisualDescendants().OfType<PathIcon>().Where(i => i.IsEffectivelyVisible).ToList();

    private static List<Button> VisibleButtons(Window window, string automationName) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.IsEffectivelyVisible && AutomationProperties.GetName(b) == automationName)
            .ToList();

    private static IEnumerable<TextBlock> VisibleTextBlocks(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible);

    /// <summary>Shows a main window with sample readings in en-US formatting; disposing the result restores the culture and the view model.</summary>
    private static Cleanup ShowWindow(
        DisplayStyle style, out MainWindow window, out MainWindowViewModel vm, out FakeUpsMonitorEvents monitor,
        UpsInfo? info = null, Action<AppSettings>? configureSettings = null)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Strings.Culture = CultureInfo.GetCultureInfo("en");

        monitor = new FakeUpsMonitorEvents();
        var settingsStore = new InMemorySettingsStore();
        settingsStore.Settings.General.DisplayStyle = style;
        configureSettings?.Invoke(settingsStore.Settings);
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
        monitor.Info = info;
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
