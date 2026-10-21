using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.History;
using CocoNut.Core.Logging;
using CocoNut.Core.Settings;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.ViewModels;

public sealed class SettingsViewModelTests : IDisposable
{
    private static readonly UpdateChecker SharedUpdateChecker = new(new HttpClient());

    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "coconut-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    private sealed record Fixture(
        SettingsViewModel ViewModel,
        InMemorySettingsStore Store,
        SettingsService Settings,
        FakeUpsMonitorEvents Monitor,
        FakePowerActions PowerActions,
        FakeAutoStartService AutoStart,
        FakeShellLauncher ShellLauncher,
        FileLoggerProvider LogProvider,
        FakeHistoryStore HistoryStore,
        FakeNotificationService Notifications);

    private Fixture Build(AppSettings? settings = null, bool autoStartSupported = true)
    {
        var store = new InMemorySettingsStore { Settings = settings ?? new AppSettings() };
        var settingsService = new SettingsService(store, store.Settings);
        var monitor = new FakeUpsMonitorEvents();
        var powerActions = new FakePowerActions();
        var autoStart = new FakeAutoStartService { IsSupported = autoStartSupported };
        var shellLauncher = new FakeShellLauncher();
        var logProvider = new FileLoggerProvider(_logDirectory);
        var navigator = new FakeWindowNavigator();
        var notifications = new FakeNotificationService();
        var historyStore = new FakeHistoryStore();

        var vm = new SettingsViewModel(
            settingsService,
            monitor,
            powerActions,
            autoStart,
            shellLauncher,
            logProvider,
            SharedUpdateChecker,
            navigator,
            notifications,
            historyStore,
            NullLogger<SettingsViewModel>.Instance);

        return new Fixture(vm, store, settingsService, monitor, powerActions, autoStart, shellLauncher, logProvider, historyStore, notifications);
    }

    [Fact]
    public void Editing_fields_does_not_mutate_the_live_settings_until_saved()
    {
        var settings = new AppSettings();
        var originalHost = settings.Connection.Host;
        var f = Build(settings);

        f.ViewModel.Host = "changed-host";

        Assert.Equal(originalHost, f.Settings.Current.Connection.Host);
        Assert.Equal("changed-host", f.ViewModel.Host);
    }

    [Fact]
    public void Empty_host_produces_a_localized_validation_error_and_blocks_save()
    {
        var f = Build();

        f.ViewModel.Host = "   ";

        Assert.Equal(Strings.Validation_HostRequired, f.ViewModel.HostError);
        Assert.True(f.ViewModel.HasErrors);
        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));
        Assert.False(f.ViewModel.ApplyCommand.CanExecute(null));
    }

    [Fact]
    public void Port_out_of_range_produces_a_localized_validation_error()
    {
        var f = Build();

        f.ViewModel.Port = 0;

        Assert.Equal(Strings.Validation_PortRange, f.ViewModel.PortError);
        Assert.True(f.ViewModel.HasErrors);
    }

    [Fact]
    public void Calibration_min_not_less_than_max_produces_a_localized_error()
    {
        var f = Build();

        f.ViewModel.InputVoltageMin = 250;
        f.ViewModel.InputVoltageMax = 200;

        Assert.Equal(Strings.Validation_CalibrationMinLessThanMax, f.ViewModel.InputVoltageRangeError);
        Assert.True(f.ViewModel.HasErrors);
    }

    [Fact]
    public void Fixing_the_only_error_clears_it_and_allows_saving_again()
    {
        var f = Build();
        f.ViewModel.Host = string.Empty;
        Assert.True(f.ViewModel.HasErrors);

        f.ViewModel.Host = "nutserver2";

        Assert.Null(f.ViewModel.HostError);
        Assert.False(f.ViewModel.HasErrors);
        Assert.True(f.ViewModel.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Poll_interval_loads_from_milliseconds_as_seconds()
    {
        var settings = new AppSettings();
        settings.Connection.PollIntervalMs = 2500;
        var f = Build(settings);

        Assert.Equal(2.5m, f.ViewModel.PollIntervalSeconds);
    }

    [Fact]
    public async Task Saving_converts_poll_interval_seconds_back_to_milliseconds()
    {
        var f = Build();

        f.ViewModel.PollIntervalSeconds = 1.5m;
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1500, f.Settings.Current.Connection.PollIntervalMs);
    }

    [Fact]
    public async Task Saving_with_no_connection_change_does_not_restart_the_monitor()
    {
        var f = Build();

        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, f.Monitor.StartCount);
        Assert.Equal(0, f.Monitor.StopCount);
        Assert.Equal(1, f.Store.SaveCount);
    }

    [Fact]
    public async Task Saving_after_changing_the_host_restarts_the_monitor_with_the_new_settings()
    {
        var f = Build();

        f.ViewModel.Host = "new-nut-host";
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Monitor.StartCount);
        Assert.Equal("new-nut-host", f.Monitor.Settings!.Host);
    }

    [Fact]
    public async Task Saving_after_changing_the_nominal_frequency_restarts_the_monitor()
    {
        var f = Build();

        f.ViewModel.InputFrequencyNominal = 60;
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Monitor.StartCount);
    }

    [Fact]
    public async Task Saving_with_an_empty_host_stops_the_monitor_instead_of_starting_it()
    {
        var f = Build();

        // ExecuteAsync (unlike Execute/CanExecute-gated UI invocation) runs unconditionally; this exercises the
        // "empty host" branch directly even though the Settings window itself would keep Save disabled for it
        // (an empty host is also a validation error - see Empty_host_produces_a_localized_validation_error_and_blocks_save).
        f.ViewModel.Host = string.Empty;
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Monitor.StopCount);
        Assert.Equal(0, f.Monitor.StartCount);
    }

    [Fact]
    public async Task Saving_twice_with_no_further_change_restarts_the_monitor_only_once()
    {
        var f = Build();

        f.ViewModel.Host = "new-nut-host";
        await f.ViewModel.SaveCommand.ExecuteAsync(null);
        await f.ViewModel.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, f.Monitor.StartCount);
    }

    [Fact]
    public async Task Save_applies_autostart_when_supported()
    {
        var f = Build(autoStartSupported: true);

        f.ViewModel.StartWithSystem = true;
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, f.AutoStart.SetEnabledCallCount);
        Assert.True(f.AutoStart.LastEnabledValue);
    }

    [Fact]
    public async Task Save_does_not_touch_autostart_when_unsupported()
    {
        var f = Build(autoStartSupported: false);

        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(0, f.AutoStart.SetEnabledCallCount);
        Assert.False(f.ViewModel.IsStartWithSystemSupported);
    }

    [Fact]
    public void Stop_immediately_disables_the_stop_delay_field()
    {
        var f = Build();

        Assert.True(f.ViewModel.IsStopDelayEnabled);
        f.ViewModel.StopImmediately = true;
        Assert.False(f.ViewModel.IsStopDelayEnabled);
    }

    [Fact]
    public void Allow_extend_delay_enables_the_grace_time_field()
    {
        var f = Build();

        Assert.False(f.ViewModel.IsExtendDelayEnabled);
        f.ViewModel.AllowExtendDelay = true;
        Assert.True(f.ViewModel.IsExtendDelayEnabled);
    }

    [Fact]
    public void Only_supported_stop_actions_are_offered()
    {
        var f = Build();
        f.PowerActions.Supported.Clear();
        f.PowerActions.Supported.Add(StopAction.Shutdown);

        // The view model reads IPowerActions.IsSupported once, in its constructor, so build a fresh one now
        // that the fake's supported set has been narrowed.
        var restricted = new SettingsViewModel(
            f.Settings, f.Monitor, f.PowerActions, f.AutoStart, f.ShellLauncher, f.LogProvider,
            SharedUpdateChecker, new FakeWindowNavigator(), new FakeNotificationService(), new FakeHistoryStore(), NullLogger<SettingsViewModel>.Instance);

        Assert.Single(restricted.StopActionOptions);
        Assert.Equal(StopAction.Shutdown, restricted.StopActionOptions[0].Value);
        Assert.Equal(StopAction.Shutdown, restricted.SelectedStopAction.Value);
    }

    [Fact]
    public void Hibernate_is_not_offered_when_it_is_unsupported()
    {
        var store = new InMemorySettingsStore { Settings = new AppSettings() };
        var powerActions = new FakePowerActions();
        powerActions.Supported.Remove(StopAction.Hibernate);

        var vm = BuildWith(store, powerActions);

        Assert.DoesNotContain(vm.StopActionOptions, o => o.Value == StopAction.Hibernate);
        Assert.Contains(vm.StopActionOptions, o => o.Value == StopAction.Shutdown);
        Assert.Contains(vm.StopActionOptions, o => o.Value == StopAction.Suspend);
    }

    [Fact]
    public void Saved_hibernate_selects_the_first_option_when_hibernate_is_unsupported()
    {
        // Documents current behaviour: the saved value is not offered, so the combo box falls back to its first
        // option (Shutdown).
        var settings = new AppSettings();
        settings.Power.StopAction = StopAction.Hibernate;
        var store = new InMemorySettingsStore { Settings = settings };
        var powerActions = new FakePowerActions();
        powerActions.Supported.Remove(StopAction.Hibernate);

        var vm = BuildWith(store, powerActions);

        Assert.Equal(StopAction.Shutdown, vm.SelectedStopAction.Value);
    }

    private SettingsViewModel BuildWith(InMemorySettingsStore store, FakePowerActions powerActions) =>
        new(
            new SettingsService(store, store.Settings),
            new FakeUpsMonitorEvents(),
            powerActions,
            new FakeAutoStartService(),
            new FakeShellLauncher(),
            new FileLoggerProvider(_logDirectory),
            SharedUpdateChecker,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new FakeHistoryStore(),
            NullLogger<SettingsViewModel>.Instance);

    [Fact]
    public void Language_options_include_english_and_every_supported_culture()
    {
        var f = Build();

        Assert.Contains(f.ViewModel.LanguageOptions, o => o.CultureCode is null); // System default
        Assert.Contains(f.ViewModel.LanguageOptions, o => o.CultureCode == "en");
        Assert.Contains(f.ViewModel.LanguageOptions, o => o.CultureCode == "de-DE");
        Assert.Contains(f.ViewModel.LanguageOptions, o => o.CultureCode == "zh-TW");
        Assert.Equal(8, f.ViewModel.LanguageOptions.Count);
    }

    [Fact]
    public async Task Selected_theme_is_saved()
    {
        var f = Build();
        Assert.Equal(AppTheme.System, f.ViewModel.SelectedTheme.Value);

        f.ViewModel.SelectedTheme = f.ViewModel.ThemeOptions.First(o => o.Value == AppTheme.Dark);
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(AppTheme.Dark, f.Settings.Current.General.Theme);
    }

    [Fact]
    public async Task Selected_display_style_is_saved()
    {
        var f = Build();
        Assert.Equal(DisplayStyle.Gauges, f.ViewModel.SelectedDisplayStyle.Value);

        f.ViewModel.SelectedDisplayStyle = f.ViewModel.DisplayStyleOptions.First(o => o.Value == DisplayStyle.Digital);
        await f.ViewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(DisplayStyle.Digital, f.Settings.Current.General.DisplayStyle);
    }

    [Fact]
    public void Display_style_option_is_loaded_from_the_settings()
    {
        var settings = new AppSettings();
        settings.General.DisplayStyle = DisplayStyle.Digital;

        var f = Build(settings);

        Assert.Equal(DisplayStyle.Digital, f.ViewModel.SelectedDisplayStyle.Value);
        Assert.Equal([DisplayStyle.Gauges, DisplayStyle.Digital], f.ViewModel.DisplayStyleOptions.Select(o => o.Value));
    }

    [Fact]
    public void Language_restart_hint_is_hidden_until_the_selection_changes()
    {
        var f = Build();
        Assert.False(f.ViewModel.IsLanguageRestartHintVisible);

        f.ViewModel.SelectedLanguage = f.ViewModel.LanguageOptions.First(o => o.CultureCode == "de-DE");

        Assert.True(f.ViewModel.IsLanguageRestartHintVisible);
    }

    [Fact]
    public void Cancel_does_not_save()
    {
        var f = Build();
        var closed = false;
        f.ViewModel.CloseRequested += (_, _) => closed = true;

        f.ViewModel.Host = "should-not-be-saved";
        f.ViewModel.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.NotEqual("should-not-be-saved", f.Settings.Current.Connection.Host);
        Assert.Equal(0, f.Store.SaveCount);
    }

    [Fact]
    public async Task Save_raises_close_requested_but_apply_does_not()
    {
        var f = Build();
        var closeCount = 0;
        f.ViewModel.CloseRequested += (_, _) => closeCount++;

        await f.ViewModel.ApplyCommand.ExecuteAsync(null);
        Assert.Equal(0, closeCount);

        await f.ViewModel.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, closeCount);
    }

    [Fact]
    public void Open_log_file_launches_the_current_log_path()
    {
        var f = Build();

        f.ViewModel.OpenLogFileCommand.Execute(null);

        Assert.Contains(f.LogProvider.CurrentLogFilePath, f.ShellLauncher.OpenedFiles);
    }

    [Fact]
    public async Task Delete_log_files_only_deletes_when_confirmed()
    {
        var f = Build();
        f.LogProvider.Enabled = true;
        var logger = f.LogProvider.CreateLogger("test");
        logger.LogInformation("hello");
        (f.LogProvider as IDisposable)!.Dispose();
        var freshProvider = new FileLoggerProvider(_logDirectory, enabled: true);
        Assert.True(File.Exists(freshProvider.CurrentLogFilePath));

        var vm = new SettingsViewModel(
            f.Settings, f.Monitor, f.PowerActions, f.AutoStart, f.ShellLauncher, freshProvider,
            SharedUpdateChecker, new FakeWindowNavigator(), new FakeNotificationService(), new FakeHistoryStore(), NullLogger<SettingsViewModel>.Instance)
        {
            ConfirmAsync = (_, _) => Task.FromResult(false),
        };

        await vm.DeleteLogFilesCommand.ExecuteAsync(null);
        Assert.True(File.Exists(freshProvider.CurrentLogFilePath));

        vm.ConfirmAsync = (_, _) => Task.FromResult(true);
        await vm.DeleteLogFilesCommand.ExecuteAsync(null);
        Assert.False(File.Exists(freshProvider.CurrentLogFilePath));
    }

    [Fact]
    public void Import_button_is_hidden_without_a_discoverable_winnut_config_file()
    {
        var f = Build();

        // No CI runner (Linux, macOS, or a clean Windows image) has a real WinNUT install, so
        // WinNutSettingsImporter.FindWinNutConfigFiles() finds nothing and the button stays hidden everywhere.
        Assert.False(f.ViewModel.ShowImportButton);
    }

    // ---- History ---------------------------------------------------------------------------------------------------

    [Fact]
    public void History_fields_load_from_the_settings()
    {
        var settings = new AppSettings();
        settings.History.Enabled = false;
        settings.History.RetentionDays = 90;
        settings.History.SampleIntervalSeconds = 30;

        var f = Build(settings);

        Assert.False(f.ViewModel.HistoryEnabled);
        Assert.Equal(90m, f.ViewModel.HistoryRetentionDays);
        Assert.Equal(30m, f.ViewModel.HistorySampleIntervalSeconds);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public void History_bounds_come_from_the_validator()
    {
        var f = Build();

        Assert.Equal(AppSettingsValidator.MinHistoryRetentionDays, f.ViewModel.HistoryRetentionMinimum);
        Assert.Equal(AppSettingsValidator.MaxHistoryRetentionDays, f.ViewModel.HistoryRetentionMaximum);
        Assert.Equal(AppSettingsValidator.MinHistorySampleIntervalSeconds, f.ViewModel.HistorySampleIntervalMinimum);
        Assert.Equal(AppSettingsValidator.MaxHistorySampleIntervalSeconds, f.ViewModel.HistorySampleIntervalMaximum);
    }

    [Fact]
    public void History_edits_stay_private_until_saved_and_then_reach_the_settings()
    {
        var f = Build();

        f.ViewModel.HistoryEnabled = false;
        f.ViewModel.HistoryRetentionDays = 14;
        f.ViewModel.HistorySampleIntervalSeconds = 60;

        Assert.True(f.Settings.Current.History.Enabled);
        Assert.Equal(30, f.Settings.Current.History.RetentionDays);

        f.ViewModel.SaveCommand.Execute(null);

        Assert.False(f.Settings.Current.History.Enabled);
        Assert.Equal(14, f.Settings.Current.History.RetentionDays);
        Assert.Equal(60, f.Settings.Current.History.SampleIntervalSeconds);
        Assert.Equal(14, f.Store.Settings.History.RetentionDays);
    }

    [Fact]
    public void History_retention_out_of_range_is_a_localized_error_that_blocks_saving()
    {
        var f = Build();

        f.ViewModel.HistoryRetentionDays = 0;

        Assert.Equal(Strings.Validation_HistoryRetentionRange, f.ViewModel.HistoryRetentionError);
        Assert.True(f.ViewModel.HasErrors);
        Assert.False(f.ViewModel.SaveCommand.CanExecute(null));

        f.ViewModel.HistoryRetentionDays = 3651;
        Assert.NotNull(f.ViewModel.HistoryRetentionError);

        f.ViewModel.HistoryRetentionDays = 3650;
        Assert.Null(f.ViewModel.HistoryRetentionError);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public void History_sample_interval_out_of_range_is_a_localized_error_that_blocks_saving()
    {
        var f = Build();

        f.ViewModel.HistorySampleIntervalSeconds = 3601;

        Assert.Equal(Strings.Validation_HistorySampleIntervalRange, f.ViewModel.HistorySampleIntervalError);
        Assert.True(f.ViewModel.HasErrors);

        f.ViewModel.HistorySampleIntervalSeconds = 0;
        Assert.NotNull(f.ViewModel.HistorySampleIntervalError);

        f.ViewModel.HistorySampleIntervalSeconds = 1;
        Assert.Null(f.ViewModel.HistorySampleIntervalError);
        Assert.False(f.ViewModel.HasErrors);
    }

    [Fact]
    public async Task Clear_history_only_clears_after_confirmation()
    {
        var f = Build();
        string? askedTitle = null;
        string? askedMessage = null;
        f.ViewModel.ConfirmAsync = (title, message) =>
        {
            askedTitle = title;
            askedMessage = message;
            return Task.FromResult(false);
        };

        await f.ViewModel.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Equal(Strings.Prefs_History_Clear_ConfirmTitle, askedTitle);
        Assert.Equal(Strings.Prefs_History_Clear_ConfirmMessage, askedMessage);
        Assert.Empty(f.HistoryStore.Cleared);
        Assert.Empty(f.Notifications.Notifications);

        f.ViewModel.ConfirmAsync = (_, _) => Task.FromResult(true);
        await f.ViewModel.ClearHistoryCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(f.HistoryStore.Cleared)); // null = every UPS
        var notification = Assert.Single(f.Notifications.Notifications);
        Assert.Equal(Strings.Prefs_History_Cleared, notification.Message);
    }

    [Fact]
    public async Task Clear_history_reports_a_store_failure_instead_of_throwing()
    {
        var f = Build();
        f.ViewModel.ConfirmAsync = (_, _) => Task.FromResult(true);
        f.HistoryStore.Failure = new IOException("locked");

        await f.ViewModel.ClearHistoryCommand.ExecuteAsync(null);

        var notification = Assert.Single(f.Notifications.Notifications);
        Assert.Equal(NotificationKind.Error, notification.Kind);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Prefs_History_ClearFailed, "locked"), notification.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_logDirectory))
        {
            try
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }
    }
}
