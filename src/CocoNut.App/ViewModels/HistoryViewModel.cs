using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CocoNut.App.Controls;
using CocoNut.App.Services;
using CocoNut.Core.Abstractions;
using CocoNut.Core.History;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the history window: lets the user pick a UPS and a time range, shows the recorded metrics as one
/// <see cref="HistoryChart"/> each plus the event list (filtered here by a debounced search over the localized
/// event name and the detail text), refreshes itself every minute while open, and exports the samples or events of
/// the selection as CSV. Store failures are shown as an error line and never thrown.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    /// <summary>Chart resolution: the store averages longer ranges into at most this many points.</summary>
    public const int MaxChartPoints = 600;

    /// <summary>The most events loaded for the list (newest first).</summary>
    public const int EventLimit = 1000;

    /// <summary>How often the open window reloads its data.</summary>
    public static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long typing in the search box must pause before the event list is filtered.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IHistoryStore _store;
    private readonly ISettingsService _settingsService;
    private readonly IFileSaveDialog _saveDialog;
    private readonly INotificationService _notifications;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HistoryViewModel> _logger;
    private readonly ITimer _refreshTimer;
    private readonly ITimer _searchTimer;

    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private bool _suppressReload;
    private bool _disposed;
    private List<HistoryEventRow> _allEvents = [];
    private DateTimeOffset _rangeStart;
    private DateTimeOffset _rangeEnd;

    public HistoryViewModel(
        IHistoryStore store,
        ISettingsService settingsService,
        IFileSaveDialog saveDialog,
        INotificationService notifications,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        ILogger<HistoryViewModel> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _saveDialog = saveDialog ?? throw new ArgumentNullException(nameof(saveDialog));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        RangeOptions =
        [
            new HistoryRangeOption(TimeSpan.FromHours(1), Strings.History_Range_Last1Hour),
            new HistoryRangeOption(TimeSpan.FromHours(6), Strings.History_Range_Last6Hours),
            new HistoryRangeOption(TimeSpan.FromHours(24), Strings.History_Range_Last24Hours),
            new HistoryRangeOption(TimeSpan.FromDays(7), Strings.History_Range_Last7Days),
            new HistoryRangeOption(TimeSpan.FromDays(30), Strings.History_Range_Last30Days),
        ];
        _selectedRange = RangeOptions[2];

        _searchTimer = _timeProvider.CreateTimer(
            _ => _dispatcher.Post(ApplyEventFilter), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _refreshTimer = _timeProvider.CreateTimer(
            _ => _dispatcher.Post(OnAutoRefresh), null, AutoRefreshInterval, AutoRefreshInterval);

        Initialization = LoadAsync(refreshUps: true);
    }

    /// <summary>The first load (known UPSes, then the data); completes even if it failed - see <see cref="ErrorMessage"/>.</summary>
    public Task Initialization { get; }

    public IReadOnlyList<HistoryRangeOption> RangeOptions { get; }

    /// <summary>Every UPS that has recorded history.</summary>
    public ObservableCollection<UpsKey> UpsOptions { get; } = [];

    /// <summary>The UPS selector is only worth showing when there is something to choose.</summary>
    public bool ShowUpsSelector => UpsOptions.Count > 1;

    /// <summary>The charts, in display order; rebuilt on every load.</summary>
    [ObservableProperty]
    private IReadOnlyList<HistoryChartSeries> _charts = [];

    /// <summary>The events of the range matching <see cref="SearchText"/>, newest first.</summary>
    public ObservableCollection<HistoryEventRow> Events { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSamplesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportEventsCommand))]
    private UpsKey? _selectedUps;

    partial void OnSelectedUpsChanged(UpsKey? value)
    {
        if (!_suppressReload && value is not null)
        {
            _ = LoadAsync(refreshUps: false);
        }
    }

    [ObservableProperty]
    private HistoryRangeOption _selectedRange;

    partial void OnSelectedRangeChanged(HistoryRangeOption value)
    {
        if (!_suppressReload)
        {
            _ = LoadAsync(refreshUps: false);
        }
    }

    [ObservableProperty]
    private string _searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => _searchTimer.Change(SearchDebounce, Timeout.InfiniteTimeSpan);

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportSamplesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportEventsCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>Set once a load finished and no UPS has any recorded history.</summary>
    [ObservableProperty]
    private bool _hasNoHistory;

    /// <summary>Set when the list is empty: no events in the range, or none matching the search.</summary>
    [ObservableProperty]
    private string? _eventsEmptyText;

    /// <summary>Set when the range holds more events than were loaded.</summary>
    [ObservableProperty]
    private string? _eventsLimitText;

    // ---- Loading ---------------------------------------------------------------------------------------------------------

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync(refreshUps: true);

    private void OnAutoRefresh()
    {
        if (!_disposed && !IsLoading && !IsExporting)
        {
            _ = LoadAsync(refreshUps: true);
        }
    }

    /// <summary>Reloads the UPS list (optionally) and the selected UPS' samples and events; a newer load cancels an older one.</summary>
    private async Task LoadAsync(bool refreshUps)
    {
        if (_disposed)
        {
            return;
        }

        var previous = _loadCts;
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _loadCts = cts;
        int version = ++_loadVersion;
        previous?.Cancel();

        IsLoading = true;
        ErrorMessage = null;

        try
        {
            if (refreshUps || UpsOptions.Count == 0)
            {
                await RefreshKnownUpsAsync(token).ConfigureAwait(true);
            }

            var ups = SelectedUps;
            HasNoHistory = ups is null;
            if (ups is null)
            {
                Charts = [];
                _allEvents = [];
                ApplyEventFilter();
                return;
            }

            DateTimeOffset end = _timeProvider.GetUtcNow();
            DateTimeOffset start = end - SelectedRange.Duration;
            var samples = await _store.GetSamplesAsync(ups, start, end, MaxChartPoints, token).ConfigureAwait(true);
            var events = await _store.GetEventsAsync(ups, start, end, null, EventLimit, token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();

            _rangeStart = start;
            _rangeEnd = end;
            Charts = BuildCharts(samples, start, end);
            _allEvents = events.OrderByDescending(e => e.Timestamp).Select(CreateRow).ToList();
            EventsLimitText = events.Count >= EventLimit ? string.Format(CultureInfo.CurrentCulture, Strings.History_Events_Limit, EventLimit) : null;
            ApplyEventFilter();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load, or the window closed.
        }
        catch (Exception ex)
        {
            // Whatever the store throws (SQLite, I/O, a corrupt file) must end up as a message, never as a crash.
            _logger.LogWarning(ex, "Failed to load the history.");
            if (version == _loadVersion)
            {
                ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.History_LoadError, ex.Message);
            }
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    private async Task RefreshKnownUpsAsync(CancellationToken cancellationToken)
    {
        var known = await _store.GetKnownUpsAsync(cancellationToken).ConfigureAwait(true);
        if (!known.SequenceEqual(UpsOptions))
        {
            _suppressReload = true;
            try
            {
                var current = SelectedUps;
                UpsOptions.Clear();
                foreach (var ups in known)
                {
                    UpsOptions.Add(ups);
                }

                OnPropertyChanged(nameof(ShowUpsSelector));
                SelectedUps = (current is not null ? known.FirstOrDefault(k => k.Equals(current)) : null)
                    ?? known.FirstOrDefault(k => k.Equals(UpsKey.From(_settingsService.Current.Connection)))
                    ?? (known.Count > 0 ? known[0] : null);
            }
            finally
            {
                _suppressReload = false;
            }
        }
    }

    private static List<HistoryChartSeries> BuildCharts(IReadOnlyList<HistorySample> samples, DateTimeOffset start, DateTimeOffset end)
    {
        HistoryChartSeries Series(string caption, string unit, string format, double? minimum, double? maximum, Func<HistorySample, double?> value) =>
            new(caption, unit, format, minimum, maximum, samples.Select(s => new HistoryChartPoint(s.Timestamp, value(s))).ToList(), start, end);

        return
        [
            Series(Strings.History_Chart_BatteryCharge, "%", "0.#", 0, 100, s => s.BatteryCharge),
            Series(Strings.History_Chart_BatteryRuntime, Strings.History_Unit_Minutes, "0.#", 0, null, s => s.BatteryRuntimeSeconds / 60d),
            Series(Strings.History_Chart_Load, "%", "0.#", 0, null, s => s.LoadPercent),
            Series(Strings.History_Chart_InputVoltage, "V", "0.#", null, null, s => s.InputVoltage),
            Series(Strings.History_Chart_OutputPower, "W", "0", 0, null, s => s.OutputPowerWatts),
        ];
    }

    private static HistoryEventRow CreateRow(HistoryEvent historyEvent) => new(
        historyEvent.Timestamp,
        historyEvent.Kind,
        historyEvent.Timestamp.ToLocalTime().ToString("G", CultureInfo.CurrentCulture),
        HistoryEventFormatter.ToLocalizedText(historyEvent.Kind),
        historyEvent.Detail ?? string.Empty);

    /// <summary>
    /// Rebuilds <see cref="Events"/> from the loaded events: a row stays when the search text occurs in its localized
    /// event name or its detail, ignoring case in the current culture (the store's own search only knows the English names).
    /// </summary>
    private void ApplyEventFilter()
    {
        if (_disposed)
        {
            return;
        }

        string search = SearchText.Trim();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        bool Matches(string text) => compare.IndexOf(text, search, CompareOptions.IgnoreCase) >= 0;

        Events.Clear();
        foreach (var row in _allEvents)
        {
            if (search.Length == 0 || Matches(row.KindText) || Matches(row.Detail))
            {
                Events.Add(row);
            }
        }

        EventsEmptyText = Events.Count > 0 ? null : _allEvents.Count == 0 ? Strings.History_Events_None : Strings.History_Events_NoMatch;
    }

    // ---- Export ----------------------------------------------------------------------------------------------------------

    private bool CanExport() => !IsExporting && SelectedUps is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportSamplesAsync() => ExportAsync(
        "samples",
        Strings.History_Export_Samples_Title,
        async ups =>
        {
            // Full resolution, not the averaged chart points.
            var samples = await _store.GetSamplesAsync(ups, _rangeStart, _rangeEnd, null).ConfigureAwait(true);
            return writer => HistoryCsvExporter.WriteSamplesAsync(writer, samples);
        });

    [RelayCommand(CanExecute = nameof(CanExport))]
    private Task ExportEventsAsync() => ExportAsync(
        "events",
        Strings.History_Export_Events_Title,
        async ups =>
        {
            var events = await _store.GetEventsAsync(ups, _rangeStart, _rangeEnd, null, int.MaxValue).ConfigureAwait(true);
            return writer => HistoryCsvExporter.WriteEventsAsync(writer, events);
        });

    /// <summary>Queries the data first (so a store failure leaves no empty file behind), then asks where to save it and writes it.</summary>
    private async Task ExportAsync(string kind, string dialogTitle, Func<UpsKey, Task<Func<TextWriter, Task>>> query)
    {
        if (SelectedUps is not { } ups)
        {
            return;
        }

        IsExporting = true;
        ErrorMessage = null;

        try
        {
            var write = await query(ups).ConfigureAwait(true);
            string fileName = BuildFileName(ups, kind, _timeProvider.GetLocalNow());
            await using var target = await _saveDialog.PickCsvAsync(dialogTitle, fileName, Strings.History_Export_FileType).ConfigureAwait(true);
            if (target is null)
            {
                return; // The user cancelled the save dialog.
            }

            await using (var writer = new StreamWriter(target.Stream, new UTF8Encoding(false), leaveOpen: true))
            {
                await write(writer).ConfigureAwait(true);
                await writer.FlushAsync().ConfigureAwait(true);
            }

            _notifications.Notify(
                Strings.Notify_Title_Info,
                string.Format(CultureInfo.CurrentCulture, Strings.History_Export_Success, target.Path),
                NotificationKind.Info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to export the history ({Kind}).", kind);
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Strings.History_ExportError, ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    /// <summary>Suggested export name, e.g. <c>coconut-ups-samples-20261019-1432.csv</c> (local time, characters invalid in file names replaced).</summary>
    internal static string BuildFileName(UpsKey ups, string kind, DateTimeOffset localNow)
    {
        // Characters Windows rejects are replaced on every platform, so an export can be moved between them.
        var invalid = Path.GetInvalidFileNameChars().Concat("\\/:*?\"<>|").ToArray();
        var name = new string(ups.UpsName.Select(c => Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c) ? '_' : c).ToArray());
        if (name.Length == 0)
        {
            name = "ups";
        }

        return $"coconut-{name}-{kind}-{localNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshTimer.Dispose();
        _searchTimer.Dispose();
        _loadCts?.Cancel();
    }
}
