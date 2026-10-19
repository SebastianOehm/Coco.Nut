using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.History;
using CocoNut.Core.Settings;
using CocoNut.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.App.Tests.ViewModels;

public sealed class HistoryViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
    private readonly List<HistoryViewModel> _created = [];

    public HistoryViewModelTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    private sealed record Fixture(
        HistoryViewModel ViewModel,
        FakeHistoryStore Store,
        FakeFileSaveDialog Dialog,
        FakeNotificationService Notifications,
        FakeTimeProvider Time,
        AppSettings Settings);

    private static UpsKey Ups(string name, string host = "nut.local") => new(host, 3493, name);

    private Fixture Build(Action<FakeHistoryStore>? seed = null, Action<AppSettings>? configure = null)
    {
        var settings = new AppSettings();
        settings.Connection.Host = "nut.local";
        settings.Connection.Port = 3493;
        settings.Connection.UpsName = "ups";
        configure?.Invoke(settings);

        var store = new FakeHistoryStore();
        seed?.Invoke(store);
        var dialog = new FakeFileSaveDialog();
        var notifications = new FakeNotificationService();
        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);

        var settingsStore = new InMemorySettingsStore { Settings = settings };
        var viewModel = new HistoryViewModel(
            store, new SettingsService(settingsStore, settings), dialog, notifications, new ImmediateUiDispatcher(), time,
            NullLogger<HistoryViewModel>.Instance);
        _created.Add(viewModel);
        return new Fixture(viewModel, store, dialog, notifications, time, settings);
    }

    private static async Task WaitIdleAsync(HistoryViewModel viewModel)
    {
        await viewModel.Initialization;
        for (int i = 0; i < 400 && viewModel.IsLoading; i++)
        {
            await Task.Delay(5);
        }
    }

    private static HistorySample Sample(int minutesAgo, double? charge = 100, double? load = 20, double? runtime = null, double? voltage = 230, double? power = null) =>
        new(Now.AddMinutes(-minutesAgo), "OL", BatteryCharge: charge, BatteryRuntimeSeconds: runtime, InputVoltage: voltage, LoadPercent: load, OutputPowerWatts: power);

    // ---- UPS selection ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Preselects_the_ups_of_the_current_connection_settings()
    {
        var f = Build(store => store.KnownUps.AddRange([Ups("other"), Ups("ups"), Ups("third")]));
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(Ups("ups"), f.ViewModel.SelectedUps);
        Assert.True(f.ViewModel.ShowUpsSelector);
        Assert.Equal(3, f.ViewModel.UpsOptions.Count);
        Assert.All(f.Store.SampleQueries, query => Assert.Equal(Ups("ups"), query.Ups));
    }

    [Fact]
    public async Task Falls_back_to_the_first_known_ups_and_hides_the_selector_for_a_single_one()
    {
        var f = Build(store => store.KnownUps.Add(Ups("only")));
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(Ups("only"), f.ViewModel.SelectedUps);
        Assert.False(f.ViewModel.ShowUpsSelector);
        Assert.False(f.ViewModel.HasNoHistory);
    }

    [Fact]
    public async Task Reports_no_history_when_no_ups_is_known()
    {
        var f = Build();
        await WaitIdleAsync(f.ViewModel);

        Assert.True(f.ViewModel.HasNoHistory);
        Assert.Null(f.ViewModel.SelectedUps);
        Assert.Empty(f.ViewModel.Charts);
        Assert.Empty(f.Store.SampleQueries);
        Assert.False(f.ViewModel.ExportSamplesCommand.CanExecute(null));
    }

    [Fact]
    public async Task Changing_the_ups_reloads_for_that_ups()
    {
        var f = Build(store => store.KnownUps.AddRange([Ups("ups"), Ups("other")]));
        await WaitIdleAsync(f.ViewModel);

        f.ViewModel.SelectedUps = Ups("other");
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(Ups("other"), f.Store.SampleQueries[^1].Ups);
        Assert.Equal(Ups("other"), f.Store.EventQueries[^1].Ups);
    }

    // ---- Range -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Initial_load_covers_the_last_24_hours_at_chart_resolution()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);

        var samples = Assert.Single(f.Store.SampleQueries);
        Assert.Equal(Now - TimeSpan.FromHours(24), samples.From);
        Assert.Equal(Now, samples.To);
        Assert.Equal(600, samples.MaxPoints);

        var events = Assert.Single(f.Store.EventQueries);
        Assert.Equal(Now - TimeSpan.FromHours(24), events.From);
        Assert.Equal(Now, events.To);
        Assert.Null(events.Search);
        Assert.Equal(1000, events.Limit);
    }

    [Fact]
    public async Task Range_options_cover_one_hour_to_thirty_days()
    {
        var f = Build();
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(
            [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24), TimeSpan.FromDays(7), TimeSpan.FromDays(30)],
            f.ViewModel.RangeOptions.Select(o => o.Duration));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 6)]
    [InlineData(3, 168)]
    [InlineData(4, 720)]
    public async Task Changing_the_range_reloads_with_the_matching_from_and_to(int optionIndex, int hours)
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        f.Time.Advance(TimeSpan.FromSeconds(10));

        f.ViewModel.SelectedRange = f.ViewModel.RangeOptions[optionIndex];
        await WaitIdleAsync(f.ViewModel);

        var query = f.Store.SampleQueries[^1];
        Assert.Equal(Now.AddSeconds(10), query.To);
        Assert.Equal(query.To - TimeSpan.FromHours(hours), query.From);
        Assert.Equal(query.From, f.Store.EventQueries[^1].From);
        Assert.All(f.ViewModel.Charts, chart =>
        {
            Assert.Equal(query.From, chart.RangeStart);
            Assert.Equal(query.To, chart.RangeEnd);
        });
    }

    [Fact]
    public async Task Refresh_reloads_with_the_current_time()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        f.Time.Advance(TimeSpan.FromSeconds(5));

        await f.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, f.Store.SampleQueries.Count);
        Assert.Equal(Now.AddSeconds(5), f.Store.SampleQueries[^1].To);
    }

    // ---- Charts -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Charts_are_mapped_per_metric_and_keep_nulls()
    {
        var f = Build(store =>
        {
            store.KnownUps.Add(Ups("ups"));
            store.Samples.AddRange(
            [
                Sample(30, charge: 100, load: 20, runtime: 1800, voltage: 231, power: 140),
                Sample(20, charge: 90, load: null, runtime: 600, voltage: null, power: 150),
                Sample(10, charge: null, load: 25, runtime: null, voltage: 229, power: null),
            ]);
        });
        await WaitIdleAsync(f.ViewModel);

        var charts = f.ViewModel.Charts;
        Assert.Equal(
            [Strings.History_Chart_BatteryCharge, Strings.History_Chart_BatteryRuntime, Strings.History_Chart_Load, Strings.History_Chart_InputVoltage, Strings.History_Chart_OutputPower],
            charts.Select(c => c.Caption));
        Assert.Equal(["%", Strings.History_Unit_Minutes, "%", "V", "W"], charts.Select(c => c.Unit));

        Assert.Equal([100d, 90d, null], charts[0].Points.Select(p => p.Value));
        Assert.Equal([30d, 10d, null], charts[1].Points.Select(p => p.Value)); // seconds shown as minutes
        Assert.Equal([20d, null, 25d], charts[2].Points.Select(p => p.Value));
        Assert.Equal([231d, null, 229d], charts[3].Points.Select(p => p.Value));
        Assert.Equal([140d, 150d, null], charts[4].Points.Select(p => p.Value));
        Assert.Equal(Now.AddMinutes(-30), charts[0].Points[0].Timestamp);

        // Percent charts are pinned to 0-100, the others scale themselves (runtime/power from zero).
        Assert.Equal((0d, 100d), (charts[0].Minimum, charts[0].Maximum));
        Assert.Equal(0d, charts[1].Minimum);
        Assert.Null(charts[3].Minimum);
        Assert.Null(charts[3].Maximum);
    }

    [Fact]
    public async Task A_metric_without_any_value_is_flagged_as_having_no_data()
    {
        var f = Build(store =>
        {
            store.KnownUps.Add(Ups("ups"));
            store.Samples.Add(Sample(10, power: null, runtime: null));
        });
        await WaitIdleAsync(f.ViewModel);

        Assert.True(f.ViewModel.Charts[0].HasData);
        Assert.False(f.ViewModel.Charts[1].HasData);
        Assert.False(f.ViewModel.Charts[4].HasData);
    }

    // ---- Events ------------------------------------------------------------------------------------------------------

    private static void SeedEvents(FakeHistoryStore store)
    {
        var ups = Ups("ups");
        store.KnownUps.Add(ups);
        store.Events.AddRange(
        [
            new HistoryEvent(Now.AddHours(-5), ups, HistoryEventKind.Connected),
            new HistoryEvent(Now.AddHours(-4), ups, HistoryEventKind.OnBattery, "charge=100%"),
            new HistoryEvent(Now.AddHours(-3), ups, HistoryEventKind.LowBattery, "runtime=120s"),
            new HistoryEvent(Now.AddHours(-2), ups, HistoryEventKind.ShutdownFailed, "Access denied by policy"),
            new HistoryEvent(Now.AddHours(-1), ups, HistoryEventKind.OnLine),
        ]);
    }

    [Fact]
    public async Task Events_are_listed_newest_first_with_localized_texts()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(
            [HistoryEventKind.OnLine, HistoryEventKind.ShutdownFailed, HistoryEventKind.LowBattery, HistoryEventKind.OnBattery, HistoryEventKind.Connected],
            f.ViewModel.Events.Select(e => e.Kind));
        var onBattery = f.ViewModel.Events.Single(e => e.Kind == HistoryEventKind.OnBattery);
        Assert.Equal(Strings.History_Event_OnBattery, onBattery.KindText);
        Assert.Equal("charge=100%", onBattery.Detail);
        Assert.Equal(Now.AddHours(-4).ToLocalTime().ToString("G", CultureInfo.CurrentCulture), onBattery.TimeText);
        Assert.Null(f.ViewModel.EventsEmptyText);
        Assert.Null(f.ViewModel.EventsLimitText);
    }

    [Fact]
    public async Task Search_matches_the_localized_event_name_ignoring_case_after_the_debounce()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        f.ViewModel.SearchText = "ON BATTERY";
        f.Time.Advance(TimeSpan.FromMilliseconds(299));
        Assert.Equal(5, f.ViewModel.Events.Count); // still debouncing

        f.Time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([HistoryEventKind.OnBattery], f.ViewModel.Events.Select(e => e.Kind));
    }

    [Fact]
    public async Task Search_matches_a_localized_name_that_differs_from_the_enum_name()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        // The enum name is "ShutdownFailed"; the displayed (and searched) text is "Stop action failed".
        f.ViewModel.SearchText = "stop action";
        f.Time.Advance(HistoryViewModel.SearchDebounce);

        Assert.Equal([HistoryEventKind.ShutdownFailed], f.ViewModel.Events.Select(e => e.Kind));
        Assert.DoesNotContain(f.Store.EventQueries, q => q.Search is not null);
    }

    [Fact]
    public async Task Search_matches_the_detail_text_and_restarts_the_debounce_on_every_keystroke()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        f.ViewModel.SearchText = "acc";
        f.Time.Advance(TimeSpan.FromMilliseconds(200));
        f.ViewModel.SearchText = "ACCESS denied";
        f.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(5, f.ViewModel.Events.Count);

        f.Time.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal([HistoryEventKind.ShutdownFailed], f.ViewModel.Events.Select(e => e.Kind));
    }

    [Fact]
    public async Task Search_without_a_match_explains_it_and_clearing_the_search_restores_the_list()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        f.ViewModel.SearchText = "zzz";
        f.Time.Advance(HistoryViewModel.SearchDebounce);
        Assert.Empty(f.ViewModel.Events);
        Assert.Equal(Strings.History_Events_NoMatch, f.ViewModel.EventsEmptyText);

        f.ViewModel.SearchText = string.Empty;
        f.Time.Advance(HistoryViewModel.SearchDebounce);
        Assert.Equal(5, f.ViewModel.Events.Count);
        Assert.Null(f.ViewModel.EventsEmptyText);
    }

    [Fact]
    public async Task An_empty_range_says_there_are_no_events()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);

        Assert.Empty(f.ViewModel.Events);
        Assert.Equal(Strings.History_Events_None, f.ViewModel.EventsEmptyText);
    }

    [Fact]
    public async Task Hitting_the_event_limit_is_announced()
    {
        var f = Build(store =>
        {
            store.KnownUps.Add(Ups("ups"));
            for (int i = 0; i < HistoryViewModel.EventLimit + 50; i++)
            {
                store.Events.Add(new HistoryEvent(Now.AddSeconds(-i - 1), Ups("ups"), HistoryEventKind.Connected));
            }
        });
        await WaitIdleAsync(f.ViewModel);

        Assert.Equal(HistoryViewModel.EventLimit, f.ViewModel.Events.Count);
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.History_Events_Limit, HistoryViewModel.EventLimit), f.ViewModel.EventsLimitText);
    }

    [Fact]
    public void Every_event_kind_has_a_localized_name()
    {
        foreach (var kind in Enum.GetValues<HistoryEventKind>())
        {
            // Each kind is backed by its own resource, not by the enum-name fallback.
            var expected = Strings.ResourceManager.GetString("History_Event_" + kind, Strings.Culture);

            Assert.False(string.IsNullOrWhiteSpace(expected));
            Assert.Equal(expected, HistoryEventFormatter.ToLocalizedText(kind));
        }
    }

    // ---- Export ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Export_samples_writes_the_full_resolution_csv_to_the_chosen_stream()
    {
        var f = Build(store =>
        {
            store.KnownUps.Add(Ups("ups"));
            store.Samples.AddRange([Sample(20, charge: 99.5), Sample(10, charge: 98)]);
        });
        await WaitIdleAsync(f.ViewModel);

        await f.ViewModel.ExportSamplesCommand.ExecuteAsync(null);

        Assert.Null(f.ViewModel.ErrorMessage);
        Assert.Equal("coconut-ups-samples-20261019-1200.csv", f.Dialog.LastSuggestedFileName);
        Assert.Equal(Strings.History_Export_Samples_Title, f.Dialog.LastTitle);
        Assert.Null(f.Store.SampleQueries[^1].MaxPoints);

        var expected = new StringWriter(CultureInfo.InvariantCulture);
        await HistoryCsvExporter.WriteSamplesAsync(expected, f.Store.Samples);
        Assert.Equal(expected.ToString(), f.Dialog.Text);

        var notification = Assert.Single(f.Notifications.Notifications);
        Assert.Equal(NotificationKind.Info, notification.Kind);
        Assert.Contains("coconut-ups-samples-20261019-1200.csv", notification.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_events_writes_every_event_of_the_range_even_when_a_search_is_active()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);
        f.ViewModel.SearchText = "battery";
        f.Time.Advance(HistoryViewModel.SearchDebounce);
        Assert.Equal(2, f.ViewModel.Events.Count);

        await f.ViewModel.ExportEventsCommand.ExecuteAsync(null);

        Assert.Equal("coconut-ups-events-20261019-1200.csv", f.Dialog.LastSuggestedFileName);
        var expected = new StringWriter(CultureInfo.InvariantCulture);
        await HistoryCsvExporter.WriteEventsAsync(expected, f.Store.Events.OrderByDescending(e => e.Timestamp));
        Assert.Equal(expected.ToString(), f.Dialog.Text);
    }

    [Fact]
    public async Task Cancelling_the_save_dialog_writes_nothing_and_stays_quiet()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        f.Dialog.Cancel = true;

        await f.ViewModel.ExportSamplesCommand.ExecuteAsync(null);
        await f.ViewModel.ExportEventsCommand.ExecuteAsync(null);

        Assert.Equal(2, f.Dialog.PickCount);
        Assert.Null(f.Dialog.Stream);
        Assert.Empty(f.Notifications.Notifications);
        Assert.Null(f.ViewModel.ErrorMessage);
        Assert.False(f.ViewModel.IsExporting);
    }

    [Fact]
    public async Task A_failing_export_shows_an_error_line_instead_of_throwing()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        f.Store.Failure = new IOException("disk full");

        await f.ViewModel.ExportSamplesCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.History_ExportError, "disk full"), f.ViewModel.ErrorMessage);
        Assert.False(f.ViewModel.IsExporting);
        Assert.Equal(0, f.Dialog.PickCount); // nothing to save, so no dialog and no empty file
        Assert.Empty(f.Notifications.Notifications);
    }

    [Theory]
    [InlineData("ups", "coconut-ups-samples-20261019-1432.csv")]
    [InlineData("my ups/1:b", "coconut-my_ups_1_b-samples-20261019-1432.csv")]
    [InlineData("", "coconut-ups-samples-20261019-1432.csv")]
    public void BuildFileName_is_filesystem_safe_and_uses_the_local_time(string upsName, string expected)
    {
        var local = new DateTimeOffset(2026, 10, 19, 14, 32, 59, TimeSpan.FromHours(2));

        Assert.Equal(expected, HistoryViewModel.BuildFileName(new UpsKey("h", 1, upsName), "samples", local));
    }

    // ---- Errors ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_store_exception_becomes_an_error_text_and_a_later_refresh_recovers()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        f.Store.Failure = new InvalidOperationException("database is locked");

        await f.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.History_LoadError, "database is locked"), f.ViewModel.ErrorMessage);
        Assert.False(f.ViewModel.IsLoading);

        f.Store.Failure = null;
        await f.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Null(f.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task A_store_that_fails_from_the_start_does_not_throw_from_the_constructor_or_initialization()
    {
        var f = Build(store => store.Failure = new IOException("unreadable"));
        await f.ViewModel.Initialization;

        Assert.Contains("unreadable", f.ViewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.False(f.ViewModel.IsLoading);
        Assert.Empty(f.ViewModel.Charts);
    }

    // ---- Auto refresh -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_data_refreshes_every_minute_until_the_view_model_is_disposed()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        Assert.Single(f.Store.SampleQueries);

        f.Time.Advance(TimeSpan.FromSeconds(59));
        await WaitIdleAsync(f.ViewModel);
        Assert.Single(f.Store.SampleQueries);

        f.Time.Advance(TimeSpan.FromSeconds(1));
        await WaitIdleAsync(f.ViewModel);
        Assert.Equal(2, f.Store.SampleQueries.Count);
        Assert.Equal(Now.AddSeconds(60), f.Store.SampleQueries[^1].To);

        f.Time.Advance(TimeSpan.FromSeconds(60));
        await WaitIdleAsync(f.ViewModel);
        Assert.Equal(3, f.Store.SampleQueries.Count);

        f.ViewModel.Dispose();
        f.Time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(20);

        Assert.Equal(3, f.Store.SampleQueries.Count);
    }

    [Fact]
    public async Task Auto_refresh_picks_up_a_newly_recorded_ups()
    {
        var f = Build(store => store.KnownUps.Add(Ups("ups")));
        await WaitIdleAsync(f.ViewModel);
        Assert.False(f.ViewModel.ShowUpsSelector);

        f.Store.KnownUps.Add(Ups("second"));
        f.Time.Advance(HistoryViewModel.AutoRefreshInterval);
        await WaitIdleAsync(f.ViewModel);

        Assert.True(f.ViewModel.ShowUpsSelector);
        Assert.Equal(Ups("ups"), f.ViewModel.SelectedUps); // the selection survives the refresh
    }

    [Fact]
    public async Task Disposing_stops_the_search_debounce_too()
    {
        var f = Build(SeedEvents);
        await WaitIdleAsync(f.ViewModel);

        f.ViewModel.SearchText = "battery";
        f.ViewModel.Dispose();
        f.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(5, f.ViewModel.Events.Count);
    }

    public void Dispose()
    {
        foreach (var viewModel in _created)
        {
            viewModel.Dispose();
        }

        CultureInfo.CurrentCulture = _originalCulture;
    }
}
