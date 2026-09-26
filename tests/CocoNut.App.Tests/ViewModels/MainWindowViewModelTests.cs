using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using CocoNut.Core.Updates;
using CocoNut.Localization;

namespace CocoNut.App.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static readonly UpdateChecker SharedUpdateChecker = new(new HttpClient());

    public MainWindowViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    private static (MainWindowViewModel ViewModel, FakeUpsMonitorEvents Monitor, FakeShutdownEvents ShutdownEvents,
        SettingsService Settings, FakeNotificationService Notifications, FakeWindowNavigator Navigator) Build(AppSettings? settings = null)
    {
        var monitor = new FakeUpsMonitorEvents();
        var shutdownEvents = new FakeShutdownEvents();
        var store = new InMemorySettingsStore { Settings = settings ?? new AppSettings() };
        var settingsService = new SettingsService(store, store.Settings);
        var notifications = new FakeNotificationService();
        var navigator = new FakeWindowNavigator();

        var vm = new MainWindowViewModel(
            monitor,
            shutdownEvents,
            settingsService,
            navigator,
            notifications,
            SharedUpdateChecker,
            new ImmediateUiDispatcher(),
            TimeProvider.System,
            requestExit: () => { });

        return (vm, monitor, shutdownEvents, settingsService, notifications, navigator);
    }

    private static UpsReading SampleReading(
        UpsStatus status = UpsStatus.OL,
        double? charge = 80,
        TimeSpan? runtime = null,
        double? inputVoltage = 230,
        double? outputVoltage = 228,
        double? frequency = 50,
        double? batteryVoltage = 13.2,
        double? load = 35,
        double? power = 180,
        bool chargeEstimated = false,
        bool runtimeEstimated = false) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        Status = status,
        BatteryCharge = charge,
        BatteryChargeEstimated = chargeEstimated,
        BatteryVoltage = batteryVoltage,
        BatteryRuntime = runtime ?? TimeSpan.FromMinutes(25),
        BatteryRuntimeEstimated = runtimeEstimated,
        InputVoltage = inputVoltage,
        InputFrequency = frequency,
        OutputVoltage = outputVoltage,
        LoadPercent = load,
        OutputPowerWatts = power,
    };

    [Fact]
    public void Construction_with_no_connection_shows_not_connected_and_no_data()
    {
        var (vm, _, _, _, _, _) = Build();

        Assert.Equal(Strings.Main_Status_NotConnected, vm.ConnectionStatusText);
        Assert.False(vm.IsConnected);
        Assert.True(vm.CanConnect);
        Assert.False(vm.CanDisconnect);
        Assert.Null(vm.InputVoltageGauge.Value);
        Assert.Equal(Strings.Common_Unavailable, vm.BatteryChargeText);
        Assert.Equal(Strings.Main_RuntimeUnknown, vm.BatteryRuntimeText);
        Assert.Empty(vm.EventLog);
    }

    [Fact]
    public void Connecting_then_connected_updates_status_text_and_logs_once()
    {
        var (vm, monitor, _, _, _, _) = Build();

        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connecting);
        monitor.RaiseStateChanged(MonitorState.Connecting, MonitorState.Connected);

        Assert.Equal(Strings.Main_Status_Connected, vm.ConnectionStatusText);
        Assert.True(vm.IsConnected);
        Assert.False(vm.CanConnect);
        Assert.True(vm.CanDisconnect);

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Log_Connected, monitor.Settings!.Host, monitor.Settings.Port);
        Assert.Contains(vm.EventLog, entry => entry.Message == expected);
    }

    [Fact]
    public void Reconnecting_successfully_notifies_but_the_initial_connect_does_not()
    {
        var (vm, monitor, _, _, notifications, _) = Build();

        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connecting);
        monitor.RaiseStateChanged(MonitorState.Connecting, MonitorState.Connected);
        Assert.Empty(notifications.Notifications);

        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Reconnecting, new IOException("dropped"));
        monitor.RaiseStateChanged(MonitorState.Reconnecting, MonitorState.Connected);

        Assert.Contains(notifications.Notifications, n => n.Message == Strings.Main_Status_Connected);
    }

    [Fact]
    public void A_failed_initial_connection_logs_the_failure_and_resets_to_no_data()
    {
        var (vm, monitor, _, _, _, _) = Build();
        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connecting);

        monitor.RaiseStateChanged(MonitorState.Connecting, MonitorState.Disconnected, new InvalidOperationException("boom"));

        Assert.Single(vm.EventLog);
        Assert.Contains("boom", vm.EventLog[0].Message);
        Assert.Equal(Strings.Common_Unavailable, vm.BatteryChargeText);
    }

    [Fact]
    public void A_reading_maps_gauges_battery_and_ups_info()
    {
        var (vm, monitor, _, _, _, _) = Build();
        monitor.Info = new UpsInfo("Acme", "Model X", "SN123", "1.0");

        monitor.RaiseReadingUpdated(SampleReading());

        Assert.Equal(230, vm.InputVoltageGauge.Value);
        Assert.Equal(228, vm.OutputVoltageGauge.Value);
        Assert.Equal(50, vm.InputFrequencyGauge.Value);
        Assert.Equal(13.2, vm.BatteryVoltageGauge.Value);
        Assert.Equal(35, vm.LoadGauge.Value);
        Assert.Equal(180, vm.PowerGauge.Value);

        Assert.Equal("Acme", vm.ManufacturerText);
        Assert.Equal("Model X", vm.ModelText);
        Assert.Equal("SN123", vm.SerialText);
        Assert.Equal("1.0", vm.FirmwareText);

        Assert.Equal("80%", vm.BatteryChargeText);
        Assert.False(vm.IsBatteryChargeEstimated);
        Assert.Equal("0:25:00", vm.BatteryRuntimeText);
    }

    [Fact]
    public void Estimated_charge_and_runtime_flip_the_estimated_flags()
    {
        var (vm, monitor, _, _, _, _) = Build();

        monitor.RaiseReadingUpdated(SampleReading(chargeEstimated: true, runtimeEstimated: true));

        Assert.True(vm.IsBatteryChargeEstimated);
        Assert.True(vm.IsBatteryRuntimeEstimated);
    }

    [Fact]
    public void Disconnecting_resets_gauges_and_texts_to_no_data()
    {
        var (vm, monitor, _, _, _, _) = Build();
        monitor.RaiseReadingUpdated(SampleReading());

        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Disconnected);

        Assert.Null(vm.InputVoltageGauge.Value);
        Assert.Null(vm.PowerGauge.Value);
        Assert.Equal(Strings.Common_Unavailable, vm.BatteryChargeText);
        Assert.Equal(Strings.Main_RuntimeUnknown, vm.BatteryRuntimeText);
        Assert.Equal(Strings.Common_Unavailable, vm.ManufacturerText);
        Assert.Empty(vm.ActiveStatusFlags);
    }

    [Fact]
    public void Going_on_battery_logs_and_notifies_and_active_flags_list_updates()
    {
        var (vm, monitor, _, _, notifications, _) = Build();
        monitor.RaiseReadingUpdated(SampleReading(status: UpsStatus.OL, charge: 90));

        monitor.RaiseStatusChanged(UpsStatus.OL, UpsStatus.OB | UpsStatus.DISCHRG);

        Assert.True(vm.IndicatorOnBattery);
        Assert.False(vm.IndicatorOnLine);
        Assert.Contains(Strings.UpsStatus_OB, vm.ActiveStatusFlags);
        Assert.Contains(Strings.UpsStatus_DISCHRG, vm.ActiveStatusFlags);

        Assert.Contains(notifications.Notifications, n => n.Kind == NotificationKind.Warning);
    }

    [Fact]
    public void Back_online_logs_and_notifies()
    {
        var (vm, monitor, _, _, notifications, _) = Build();

        monitor.RaiseStatusChanged(UpsStatus.OB, UpsStatus.OL);

        Assert.Contains(vm.EventLog, e => e.Message == Strings.Main_Status_OnLine);
        Assert.Contains(notifications.Notifications, n => n.Message == Strings.Main_Status_OnLine);
    }

    [Fact]
    public void Low_battery_and_battery_ok_are_logged_without_a_notification()
    {
        var (vm, monitor, _, _, notifications, _) = Build();

        monitor.RaiseStatusChanged(UpsStatus.OB, UpsStatus.OB | UpsStatus.LB);
        monitor.RaiseStatusChanged(UpsStatus.OB | UpsStatus.LB, UpsStatus.OB);

        Assert.Contains(vm.EventLog, e => e.Message == Strings.Main_Status_LowBattery);
        Assert.Contains(vm.EventLog, e => e.Message == Strings.Main_Status_BatteryOk);
        Assert.DoesNotContain(notifications.Notifications, n => n.Message == Strings.Main_Status_LowBattery);
    }

    [Fact]
    public void Connection_lost_logs_and_notifies_with_host_and_port()
    {
        var (vm, monitor, _, _, notifications, _) = Build();

        monitor.RaiseConnectionLost(new IOException("reset"));

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Main_Status_LostConnection, monitor.Settings!.Host, monitor.Settings.Port);
        Assert.Contains(vm.EventLog, e => e.Message == expected);
        Assert.Contains(notifications.Notifications, n => n.Message == expected && n.Kind == NotificationKind.Warning);
    }

    [Fact]
    public void Event_log_caps_at_200_entries_newest_first()
    {
        var (vm, monitor, _, _, _, _) = Build();

        for (var i = 0; i < 205; i++)
        {
            monitor.Settings = new ConnectionSettings { Host = $"host{i}", Port = 3493 };
            monitor.RaiseConnectionLost();
        }

        Assert.Equal(MainWindowViewModel.MaxEventLogEntries, vm.EventLog.Count);
        Assert.Contains("host204", vm.EventLog[0].Message);
        Assert.Contains("host5", vm.EventLog[^1].Message);
    }

    [Fact]
    public void Power_gauge_maximum_only_grows_within_a_session()
    {
        var (vm, monitor, _, _, _, _) = Build();

        monitor.RaiseReadingUpdated(SampleReading(load: 20, power: 3000)); // nominal 15000 -> rounds up to 15000
        Assert.Equal(15_000, vm.PowerGauge.Maximum);

        monitor.RaiseReadingUpdated(SampleReading(load: 50, power: 100)); // nominal 200 -> must not shrink the max
        Assert.Equal(15_000, vm.PowerGauge.Maximum);
    }

    [Fact]
    public void Connecting_resets_the_power_gauge_maximum_for_a_new_session()
    {
        var (vm, monitor, _, settings, _, _) = Build();
        monitor.RaiseReadingUpdated(SampleReading(load: 20, power: 3000));
        Assert.Equal(15_000, vm.PowerGauge.Maximum);

        vm.ConnectCommand.Execute(null);

        Assert.Equal(PowerGaugeMaxCalculator.DefaultMax, vm.PowerGauge.Maximum);
        Assert.Equal(1, monitor.StartCount);
    }

    [Fact]
    public void Calibration_settings_drive_gauge_ranges()
    {
        var settings = new AppSettings();
        settings.Calibration.InputVoltageMin = 200;
        settings.Calibration.InputVoltageMax = 260;
        var (vm, _, _, _, _, _) = Build(settings);

        Assert.Equal(200, vm.InputVoltageGauge.Minimum);
        Assert.Equal(260, vm.InputVoltageGauge.Maximum);
        Assert.NotEmpty(vm.InputVoltageGauge.Ranges);
    }

    [Fact]
    public void Settings_changed_reapplies_calibration()
    {
        var (vm, _, _, settings, _, _) = Build();

        var updated = settings.Current.Clone();
        updated.Calibration.LoadMax = 50;
        settings.Save(updated);

        Assert.Equal(50, vm.LoadGauge.Maximum);
    }

    [Fact]
    public void Toggling_auto_reconnect_persists_it()
    {
        var (vm, _, _, settings, _, _) = Build();

        vm.AutoReconnectEnabled = true;

        Assert.True(settings.Current.Connection.AutoReconnect);
    }

    [Fact]
    public void Shutdown_pending_and_cancelled_are_logged_and_cancelled_notifies()
    {
        var (vm, _, shutdownEvents, _, notifications, _) = Build();
        var countdown = new ShutdownCountdown(TimeProvider.System);

        shutdownEvents.RaisePending(new ShutdownPendingEventArgs(ShutdownReason.BatteryChargeFloor, countdown));
        shutdownEvents.RaiseCancelled(ShutdownReason.PowerRestored);

        Assert.Contains(vm.EventLog, e => e.Message == Strings.Log_ShutdownStart);
        Assert.Contains(vm.EventLog, e => e.Message == Strings.Log_ShutdownCancelled);
        Assert.Contains(notifications.Notifications, n => n.Message == Strings.Shutdown_Cancelled_Notify);

        countdown.Dispose();
    }

    [Fact]
    public void Shutdown_executing_logs_the_configured_stop_action()
    {
        var settings = new AppSettings();
        settings.Power.StopAction = StopAction.Hibernate;
        var (vm, _, shutdownEvents, _, _, _) = Build(settings);

        shutdownEvents.RaiseExecuting(ShutdownReason.BatteryChargeFloor);

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Log_ShutdownExecuting, Strings.StopAction_Hibernate);
        Assert.Contains(vm.EventLog, e => e.Message == expected);
    }

    [Fact]
    public void Shutdown_executing_in_dry_run_appends_the_dry_run_hint()
    {
        var (vm, _, shutdownEvents, _, _, _) = Build();
        shutdownEvents.DryRun = true;

        shutdownEvents.RaiseExecuting(ShutdownReason.ForcedShutdown);

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Log_ShutdownExecuting_DryRun, Strings.StopAction_Shutdown);
        Assert.Contains(vm.EventLog, e => e.Message == expected);
        Assert.DoesNotContain(vm.EventLog, e => e.Message == string.Format(CultureInfo.CurrentCulture, Strings.Log_ShutdownExecuting, Strings.StopAction_Shutdown));
    }

    [Fact]
    public void Disconnect_and_show_commands_delegate_to_the_monitor_and_navigator()
    {
        var (vm, monitor, _, _, _, navigator) = Build();

        vm.DisconnectCommand.Execute(null);
        vm.ShowSettingsCommand.Execute(null);
        vm.ShowUpsVariablesCommand.Execute(null);
        vm.ShowAboutCommand.Execute(null);

        Assert.Equal(1, monitor.StopCount);
        Assert.Equal(1, navigator.ShowSettingsCount);
        Assert.Equal(1, navigator.ShowUpsVariablesCount);
        Assert.Equal(1, navigator.ShowAboutCount);
    }
}
