using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.History;
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

        Assert.Equal("Acme", vm.Manufacturer.Text);
        Assert.Equal("Model X", vm.Model.Text);
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
        Assert.Equal(Strings.Common_Unavailable, vm.Manufacturer.Text);
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
    public void Gauges_lists_the_six_values_in_display_order()
    {
        var (vm, _, _, _, _, _) = Build();

        Assert.Equal(
            [vm.InputVoltageGauge, vm.OutputVoltageGauge, vm.InputFrequencyGauge, vm.BatteryVoltageGauge, vm.LoadGauge, vm.PowerGauge],
            vm.Gauges);
    }

    [Fact]
    public void The_display_style_defaults_to_gauges_and_follows_the_loaded_settings()
    {
        var (vm, _, _, _, _, _) = Build();
        Assert.False(vm.IsDigitalDisplay);
        Assert.True(vm.IsGaugeDisplay);

        var settings = new AppSettings();
        settings.General.DisplayStyle = DisplayStyle.Digital;
        var (digital, _, _, _, _, _) = Build(settings);
        Assert.True(digital.IsDigitalDisplay);
        Assert.False(digital.IsGaugeDisplay);
    }

    [Fact]
    public void Choosing_the_digital_display_persists_it_and_choosing_gauges_again_reverts_it()
    {
        var (vm, _, _, settings, _, _) = Build();

        vm.IsDigitalDisplay = true;
        Assert.Equal(DisplayStyle.Digital, settings.Current.General.DisplayStyle);
        Assert.False(vm.IsGaugeDisplay);

        vm.IsGaugeDisplay = true; // what the menu's "Gauges" radio item does
        Assert.False(vm.IsDigitalDisplay);
        Assert.Equal(DisplayStyle.Gauges, settings.Current.General.DisplayStyle);
    }

    [Fact]
    public void Changing_the_display_style_keeps_the_other_settings()
    {
        var (vm, _, _, settings, _, _) = Build();
        var updated = settings.Current.Clone();
        updated.Connection.Host = "ups.example.org";
        settings.Save(updated);

        vm.IsDigitalDisplay = true;

        Assert.Equal("ups.example.org", settings.Current.Connection.Host);
    }

    [Fact]
    public void Settings_changed_with_the_digital_display_switches_the_view_model_without_saving_again()
    {
        var (vm, _, _, settings, _, _) = Build();
        var saves = 0;
        settings.SettingsChanged += (_, _) => saves++;

        var updated = settings.Current.Clone();
        updated.General.DisplayStyle = DisplayStyle.Digital;
        settings.Save(updated);

        Assert.True(vm.IsDigitalDisplay);
        Assert.Equal(1, saves);

        var back = settings.Current.Clone();
        back.General.DisplayStyle = DisplayStyle.Gauges;
        settings.Save(back);

        Assert.False(vm.IsDigitalDisplay);
        Assert.Equal(2, saves);
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
        vm.ShowHistoryCommand.Execute(null);
        vm.ShowAboutCommand.Execute(null);

        Assert.Equal(1, monitor.StopCount);
        Assert.Equal(1, navigator.ShowSettingsCount);
        Assert.Equal(1, navigator.ShowUpsVariablesCount);
        Assert.Equal(1, navigator.ShowHistoryCount);
        Assert.Equal(1, navigator.ShowAboutCount);
    }

    // ---- Manually entered manufacturer and model ------------------------------------------------------------------

    private static readonly UpsKey ConnectedUps = new("nutserver", 3493, "ups");

    private static (MainWindowViewModel ViewModel, FakeUpsMonitorEvents Monitor, SettingsService Settings) BuildConnected(
        UpsInfo info, AppSettings? settings = null)
    {
        var (vm, monitor, _, settingsService, _, _) = Build(settings);
        monitor.Info = info;
        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);
        monitor.RaiseReadingUpdated(SampleReading());
        return (vm, monitor, settingsService);
    }

    private static UpsInfo NotDetected() => new(UpsInfo.UnknownValue, UpsInfo.UnknownValue, "SN123", "1.0");

    [Theory]
    [InlineData("Unknown")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("  ")]
    public void A_value_the_ups_does_not_report_shows_unavailable_and_can_be_overridden(string reported)
    {
        var (vm, _, _) = BuildConnected(new UpsInfo(reported, reported, "SN123", "1.0"));

        Assert.Equal(Strings.Common_Unavailable, vm.Manufacturer.Text);
        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Manufacturer.IsOverridden);
        Assert.True(vm.Manufacturer.CanOverride);
        Assert.True(vm.Model.CanOverride);
    }

    [Fact]
    public void A_reported_value_is_shown_plainly_and_cannot_be_overridden()
    {
        var (vm, _, _) = BuildConnected(new UpsInfo("Acme", "Unknown", "SN123", "1.0"));

        Assert.Equal("Acme", vm.Manufacturer.Text);
        Assert.False(vm.Manufacturer.IsOverridden);
        Assert.False(vm.Manufacturer.CanOverride);
        Assert.False(vm.Manufacturer.BeginEditCommand.CanExecute(null));
        Assert.True(vm.Model.CanOverride, "the two fields are independent");
    }

    [Fact]
    public void Nothing_can_be_overridden_while_disconnected_or_without_info()
    {
        var (vm, monitor, _, _, _, _) = Build();
        Assert.False(vm.Manufacturer.CanOverride);

        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);
        monitor.RaiseReadingUpdated(SampleReading()); // Info is still null
        Assert.False(vm.Manufacturer.CanOverride);
        Assert.False(vm.Model.CanOverride);

        monitor.Info = NotDetected();
        monitor.RaiseReadingUpdated(SampleReading());
        Assert.True(vm.Manufacturer.CanOverride);

        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Reconnecting, new IOException("dropped"));
        Assert.False(vm.Manufacturer.CanOverride);
        Assert.Equal(Strings.Common_Unavailable, vm.Manufacturer.Text);
    }

    [Fact]
    public void Saving_an_entry_shows_it_marks_it_overridden_and_persists_it_for_the_connected_ups()
    {
        var (vm, _, settings) = BuildConnected(NotDetected());

        vm.Manufacturer.BeginEditCommand.Execute(null);
        Assert.True(vm.Manufacturer.IsEditing);
        Assert.Equal("", vm.Manufacturer.EditText);
        vm.Manufacturer.EditText = "  Acme Corp ";
        vm.Manufacturer.SaveEditCommand.Execute(null);

        Assert.False(vm.Manufacturer.IsEditing);
        Assert.Equal("Acme Corp", vm.Manufacturer.Text);
        Assert.True(vm.Manufacturer.IsOverridden);
        Assert.True(vm.Manufacturer.CanOverride);
        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
        var entry = Assert.Single(settings.Current.InfoOverrides.Entries);
        Assert.Equal(("nutserver", 3493, "ups", "Acme Corp", null), (entry.Host, entry.Port, entry.UpsName, entry.Manufacturer, entry.Model));
    }

    [Fact]
    public void Editing_an_existing_entry_starts_with_its_value_and_an_empty_save_clears_it()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, ConnectedUps, UpsInfoField.Model, "X-1000");
        var (vm, _, service) = BuildConnected(NotDetected(), settings);
        Assert.Equal("X-1000", vm.Model.Text);
        Assert.True(vm.Model.IsOverridden);

        vm.Model.BeginEditCommand.Execute(null);
        Assert.Equal("X-1000", vm.Model.EditText);
        vm.Model.EditText = "   ";
        vm.Model.SaveEditCommand.Execute(null);

        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
        Assert.True(vm.Model.CanOverride);
        Assert.Empty(service.Current.InfoOverrides.Entries);
    }

    [Fact]
    public void Cancelling_leaves_the_text_and_the_settings_untouched()
    {
        var (vm, _, settings) = BuildConnected(NotDetected());
        var current = settings.Current;

        vm.Model.BeginEditCommand.Execute(null);
        vm.Model.EditText = "Typed but cancelled";
        vm.Model.CancelEditCommand.Execute(null);

        Assert.False(vm.Model.IsEditing);
        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
        Assert.Same(current, settings.Current);
        Assert.Empty(settings.Current.InfoOverrides.Entries);
    }

    [Fact]
    public void An_open_editor_survives_the_next_reading()
    {
        var (vm, monitor, _) = BuildConnected(NotDetected());
        vm.Model.BeginEditCommand.Execute(null);
        vm.Model.EditText = "X-";

        monitor.RaiseReadingUpdated(SampleReading());

        Assert.True(vm.Model.IsEditing);
        Assert.Equal("X-", vm.Model.EditText);
    }

    [Fact]
    public void A_reported_value_wins_over_a_stored_override_which_is_kept()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, ConnectedUps, UpsInfoField.Manufacturer, "Mine");
        var (vm, monitor, service) = BuildConnected(new UpsInfo("Acme", "Unknown", "SN123", "1.0"), settings);

        Assert.Equal("Acme", vm.Manufacturer.Text);
        Assert.False(vm.Manufacturer.IsOverridden);
        Assert.False(vm.Manufacturer.CanOverride);
        Assert.Equal("Mine", UpsInfoOverrideResolver.GetOverride(service.Current, ConnectedUps, UpsInfoField.Manufacturer));

        // The UPS stops reporting it (e.g. a firmware change): the stored entry applies again.
        monitor.Info = NotDetected();
        monitor.RaiseReadingUpdated(SampleReading());

        Assert.Equal("Mine", vm.Manufacturer.Text);
        Assert.True(vm.Manufacturer.IsOverridden);
    }

    [Fact]
    public void An_override_of_another_ups_is_not_applied()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, new UpsKey("nutserver", 3493, "other-ups"), UpsInfoField.Model, "Other");
        UpsInfoOverrideResolver.SetOverride(settings, new UpsKey("nutserver", 3494, "ups"), UpsInfoField.Model, "Other port");
        var (vm, _, _) = BuildConnected(NotDetected(), settings);

        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
    }

    [Fact]
    public void The_host_of_a_stored_override_matches_case_insensitively()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, new UpsKey("NUTSERVER", 3493, "ups"), UpsInfoField.Model, "X-1000");

        var (vm, _, _) = BuildConnected(NotDetected(), settings);

        Assert.Equal("X-1000", vm.Model.Text);
        Assert.True(vm.Model.IsOverridden);
    }

    [Fact]
    public void Disconnecting_resets_the_entries_closes_the_editor_and_keeps_the_stored_override()
    {
        var settings = new AppSettings();
        UpsInfoOverrideResolver.SetOverride(settings, ConnectedUps, UpsInfoField.Model, "X-1000");
        var (vm, monitor, service) = BuildConnected(NotDetected(), settings);
        vm.Manufacturer.BeginEditCommand.Execute(null);

        monitor.RaiseStateChanged(MonitorState.Connected, MonitorState.Disconnected);

        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
        Assert.False(vm.Model.CanOverride);
        Assert.False(vm.Manufacturer.IsEditing);
        Assert.Single(service.Current.InfoOverrides.Entries);
    }

    [Fact]
    public void A_settings_change_from_elsewhere_re_resolves_the_displayed_values()
    {
        var (vm, _, service) = BuildConnected(NotDetected());
        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);

        var changed = service.Current.Clone();
        UpsInfoOverrideResolver.SetOverride(changed, ConnectedUps, UpsInfoField.Model, "From elsewhere");
        service.Save(changed);

        Assert.Equal("From elsewhere", vm.Model.Text);
        Assert.True(vm.Model.IsOverridden);

        service.Save(new AppSettings());

        Assert.Equal(Strings.Common_Unavailable, vm.Model.Text);
        Assert.False(vm.Model.IsOverridden);
    }
}
