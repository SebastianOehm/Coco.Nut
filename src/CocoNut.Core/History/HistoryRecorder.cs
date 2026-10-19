using System.Globalization;
using System.Threading.Channels;
using CocoNut.Core.Actions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.History;

/// <summary>
/// Records what <see cref="UpsMonitor"/> and <see cref="ShutdownCoordinator"/> report into an
/// <see cref="IHistoryStore"/>: readings (throttled to <see cref="HistorySettings.SampleIntervalSeconds"/>, buffered
/// and flushed once a minute) and events (power failures, connection losses, shutdown steps). Event handlers only
/// queue work, so they never block or throw into the monitor's thread; one background loop performs every database
/// write in order. Database failures are logged and swallowed - if the database cannot even be opened the recorder
/// switches itself off and the application carries on without history.
/// </summary>
public sealed class HistoryRecorder : IAsyncDisposable
{
    /// <summary>
    /// Start of the detail of a shutdown event whose reason is <see cref="ShutdownReason.CustomRule"/>: the rule's name
    /// follows it. Other reasons are stored as the bare <see cref="ShutdownReason"/> name.
    /// </summary>
    public const string CustomRuleDetailPrefix = "CustomRule: ";

    /// <summary>How often buffered samples are written to the store.</summary>
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(60);

    /// <summary>How often old data is pruned and downsampled (and once at start).</summary>
    internal static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    /// <summary>How long a shutdown waits for the final flush before powering off regardless.</summary>
    internal static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(2);

    private readonly UpsMonitor _monitor;
    private readonly ShutdownCoordinator _coordinator;
    private readonly IHistoryStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly Func<HistorySettings> _settingsProvider;
    private readonly ILogger _logger;
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;
    private readonly ITimer _flushTimer;
    private readonly ITimer _pruneTimer;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly Lock _gate = new();
    private readonly List<HistorySample> _buffer = [];
    private UpsKey? _bufferKey;
    private DateTimeOffset? _lastSampleAt;
    private volatile bool _failed;
    private int _disposed;

    public HistoryRecorder(
        UpsMonitor monitor,
        ShutdownCoordinator coordinator,
        IHistoryStore store,
        TimeProvider timeProvider,
        Func<HistorySettings> settingsProvider,
        ILogger<HistoryRecorder> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _worker = Task.Run(RunWorkerAsync);

        // Open the database and prune first; everything queued by the handlers below runs after it.
        Enqueue(StartAsync);

        _flushTimer = _timeProvider.CreateTimer(_ => Enqueue(FlushSamplesAsync), null, FlushInterval, FlushInterval);
        _pruneTimer = _timeProvider.CreateTimer(_ => Enqueue(PruneAsync), null, PruneInterval, PruneInterval);

        _monitor.ReadingUpdated += OnReadingUpdated;
        _monitor.StatusChanged += OnStatusChanged;
        _monitor.StateChanged += OnStateChanged;
        _monitor.ConnectionLost += OnConnectionLost;
        _coordinator.ShutdownPending += OnShutdownPending;
        _coordinator.RuleFired += OnRuleFired;
        _coordinator.ShutdownCancelled += OnShutdownCancelled;
        _coordinator.ShutdownExecuting += OnShutdownExecuting;
        _coordinator.ShutdownFailed += OnShutdownFailed;
    }

    private bool IsRecording => !_failed && _settingsProvider().Enabled;

    private void OnReadingUpdated(object? sender, UpsReading reading) =>
        Guard(() => AddSample(reading, force: false));

    /// <summary>Buffers <paramref name="reading"/> unless the sample interval has not elapsed since the last one.</summary>
    private void AddSample(UpsReading reading, bool force)
    {
        if (!IsRecording || _monitor.Settings is not { } connection)
        {
            return;
        }

        var key = UpsKey.From(connection);
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settingsProvider().SampleIntervalSeconds));

        lock (_gate)
        {
            var sameUps = Equals(_bufferKey, key);
            if (_lastSampleAt is { } last && sameUps && (force ? reading.Timestamp <= last : reading.Timestamp - last < interval))
            {
                return;
            }

            if (_bufferKey is not null && !sameUps)
            {
                // The monitor now points at another UPS: hand over what was collected for the old one.
                var previous = TakeBufferLocked();
                if (previous is not null)
                {
                    Enqueue(() => AppendSamplesAsync(previous.Value.Key, previous.Value.Samples));
                }
            }

            _bufferKey = key;
            _lastSampleAt = reading.Timestamp;
            _buffer.Add(HistorySample.FromReading(reading));
        }
    }

    private void OnStatusChanged(object? sender, UpsStatusChangedEventArgs e) =>
        Guard(() =>
        {
            var detail = DescribeReading(_monitor.LastReading);
            if (e.NewlyActive.HasFlag(UpsStatus.OB))
            {
                QueueEvent(HistoryEventKind.OnBattery, detail);
            }

            if (e.NewlyCleared.HasFlag(UpsStatus.OB))
            {
                QueueEvent(HistoryEventKind.OnLine, detail);
            }

            if (e.NewlyActive.HasFlag(UpsStatus.LB))
            {
                QueueEvent(HistoryEventKind.LowBattery, detail);
            }

            if (e.NewlyActive.HasFlag(UpsStatus.FSD))
            {
                QueueEvent(HistoryEventKind.ForcedShutdown, detail);
            }
        });

    private void OnStateChanged(object? sender, MonitorStateChangedEventArgs e) =>
        Guard(() =>
        {
            if (e.NewState == MonitorState.Connected)
            {
                QueueEvent(HistoryEventKind.Connected, null);
            }
            else if (e.NewState == MonitorState.Disconnected
                && e.OldState is MonitorState.Connecting or MonitorState.Reconnecting
                && e.Error is not null)
            {
                // ConnectionLost is only raised for an established connection that breaks; a failed attempt ends here.
                QueueEvent(HistoryEventKind.ConnectionLost, $"Connecting failed: {DescribeException(e.Error)}");
            }
        });

    private void OnConnectionLost(object? sender, Exception? error) =>
        Guard(() => QueueEvent(HistoryEventKind.ConnectionLost, error is null ? null : DescribeException(error)));

    private void OnShutdownPending(object? sender, ShutdownPendingEventArgs e) =>
        Guard(() => QueueEvent(HistoryEventKind.ShutdownPending, DescribeReason(e.Reason, e.RuleName)));

    private void OnRuleFired(object? sender, RuleFiredEventArgs e) =>
        Guard(() => QueueEvent(HistoryEventKind.RuleFired, e.Rule.Name));

    private void OnShutdownCancelled(object? sender, ShutdownReason reason) =>
        Guard(() => QueueEvent(HistoryEventKind.ShutdownCancelled, reason.ToString()));

    private void OnShutdownFailed(object? sender, Exception error) =>
        Guard(() => QueueEvent(HistoryEventKind.ShutdownFailed, DescribeException(error)));

    private void OnShutdownExecuting(object? sender, ShutdownReason reason) =>
        Guard(() =>
        {
            QueueEvent(HistoryEventKind.ShutdownExecuting, DescribeReason(reason, _coordinator.PendingRuleName));
            if (!IsRecording)
            {
                return;
            }

            // The machine may be gone right after this: write the event and the buffered samples first, but never
            // hold the shutdown up for longer than the timeout. The reading that triggered the stop is not buffered
            // yet when the coordinator's handler runs first, so add it regardless of the sample interval.
            if (_monitor.LastReading is { } last)
            {
                AddSample(last, force: true);
            }

            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(async () =>
            {
                try
                {
                    await FlushSamplesAsync().ConfigureAwait(false);
                }
                finally
                {
                    done.TrySetResult();
                }
            });

            try
            {
                done.Task.WaitAsync(ShutdownFlushTimeout, _timeProvider).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timed out flushing the history before the shutdown.");
            }
        });

    private void QueueEvent(HistoryEventKind kind, string? detail)
    {
        if (!IsRecording || _monitor.Settings is not { } connection)
        {
            return;
        }

        var historyEvent = new HistoryEvent(_timeProvider.GetUtcNow(), UpsKey.From(connection), kind, detail);
        Enqueue(async () =>
        {
            if (_failed)
            {
                return; // The database turned out to be unusable after this event was queued.
            }

            try
            {
                await _store.AppendEventAsync(historyEvent, _disposeCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write history event {Kind}.", kind);
            }
        });
    }

    private async Task StartAsync()
    {
        try
        {
            await _store.InitializeAsync(_disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!_failed)
            {
                _failed = true;
                _logger.LogError(ex, "The history database could not be opened; history recording is disabled.");
            }

            return;
        }

        await PruneAsync().ConfigureAwait(false);
    }

    private async Task PruneAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        try
        {
            var retention = TimeSpan.FromDays(Math.Max(1, _settingsProvider().RetentionDays));
            await _store.PruneAsync(_timeProvider.GetUtcNow(), retention, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prune the history database.");
        }
    }

    private async Task FlushSamplesAsync()
    {
        (UpsKey Key, List<HistorySample> Samples)? taken;
        lock (_gate)
        {
            taken = TakeBufferLocked();
        }

        if (taken is not null && IsRecording)
        {
            await AppendSamplesAsync(taken.Value.Key, taken.Value.Samples).ConfigureAwait(false);
        }
    }

    private async Task AppendSamplesAsync(UpsKey key, List<HistorySample> samples)
    {
        if (_failed)
        {
            return;
        }

        try
        {
            await _store.AppendSamplesAsync(key, samples, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write {Count} history samples; they are dropped.", samples.Count);
        }
    }

    /// <summary>Empties the sample buffer. The throttle position is kept so flushing does not cause an extra sample.</summary>
    private (UpsKey Key, List<HistorySample> Samples)? TakeBufferLocked()
    {
        if (_buffer.Count == 0 || _bufferKey is null)
        {
            return null;
        }

        var samples = new List<HistorySample>(_buffer);
        _buffer.Clear();
        return (_bufferKey, samples);
    }

    private void Enqueue(Func<Task> work) => _work.Writer.TryWrite(work);

    private async Task RunWorkerAsync()
    {
        await foreach (var work in _work.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A history recorder task failed.");
            }
        }
    }

    /// <summary>Event handlers run on the monitor's thread: nothing may escape them.</summary>
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error in the history recorder.");
        }
    }

    private static string? DescribeReading(UpsReading? reading)
    {
        if (reading is null)
        {
            return null;
        }

        var parts = new List<string> { $"status={reading.RawStatus}" };
        if (reading.BatteryCharge is { } charge)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"charge={charge:0.#}%"));
        }

        if (reading.BatteryRuntime is { } runtime)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"runtime={runtime.TotalSeconds:0}s"));
        }

        return string.Join(", ", parts);
    }

    private static string DescribeReason(ShutdownReason reason, string? ruleName) =>
        reason == ShutdownReason.CustomRule && !string.IsNullOrWhiteSpace(ruleName)
            ? CustomRuleDetailPrefix + ruleName
            : reason.ToString();

    private static string DescribeException(Exception error) => $"{error.GetType().Name}: {error.Message}";

    /// <summary>
    /// Unsubscribes, flushes the buffered samples and waits (bounded) for the queued writes. The store is not
    /// disposed - its owner does that afterwards.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _monitor.ReadingUpdated -= OnReadingUpdated;
        _monitor.StatusChanged -= OnStatusChanged;
        _monitor.StateChanged -= OnStateChanged;
        _monitor.ConnectionLost -= OnConnectionLost;
        _coordinator.ShutdownPending -= OnShutdownPending;
        _coordinator.RuleFired -= OnRuleFired;
        _coordinator.ShutdownCancelled -= OnShutdownCancelled;
        _coordinator.ShutdownExecuting -= OnShutdownExecuting;
        _coordinator.ShutdownFailed -= OnShutdownFailed;

        await _flushTimer.DisposeAsync().ConfigureAwait(false);
        await _pruneTimer.DisposeAsync().ConfigureAwait(false);

        Enqueue(FlushSamplesAsync);
        _work.Writer.TryComplete();
        try
        {
            await _worker.WaitAsync(ShutdownFlushTimeout * 2, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Timed out waiting for the history recorder to finish writing.");
        }

        // Not disposed: a worker that outlived the timeout above may still read the token.
        await _disposeCts.CancelAsync().ConfigureAwait(false);
    }
}
