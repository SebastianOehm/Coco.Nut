using CocoNut.Core.Abstractions;
using CocoNut.Core.History;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Tests.Fakes;
using CocoNut.Core.Tests.Monitoring;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.History;

public class HistoryRecorderTests
{
    private sealed class FakeStore : IHistoryStore
    {
        public EventCollector<(UpsKey Ups, IReadOnlyList<HistorySample> Samples)> SampleBatches { get; } = new();
        public EventCollector<HistoryEvent> Events { get; } = new();
        public EventCollector<(DateTimeOffset Now, TimeSpan Retention)> Prunes { get; } = new();
        public int InitializeCalls { get; private set; }

        /// <summary>When set, the next calls of the respective kind throw this.</summary>
        public Exception? InitializeError { get; set; }
        public Exception? AppendError { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            InitializeCalls++;
            return InitializeError is { } error ? Task.FromException(error) : Task.CompletedTask;
        }

        public Task AppendSamplesAsync(UpsKey ups, IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken = default)
        {
            if (AppendError is { } error)
            {
                return Task.FromException(error);
            }

            SampleBatches.Add((ups, samples));
            return Task.CompletedTask;
        }

        public Task AppendEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default)
        {
            if (AppendError is { } error)
            {
                return Task.FromException(error);
            }

            Events.Add(historyEvent);
            return Task.CompletedTask;
        }

        public Task PruneAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken = default)
        {
            Prunes.Add((now, retention));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<HistorySample>> GetSamplesAsync(
            UpsKey ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, int? maxPoints, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(
            UpsKey? ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, string? search, int limit, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<UpsKey>> GetKnownUpsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(UpsKey? ups, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePowerActions : IPowerActions
    {
        public Exception? ThrowOnExecute { get; set; }

        public bool IsSupported(StopAction action) => true;

        public Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default) =>
            ThrowOnExecute is { } error ? Task.FromException(error) : Task.CompletedTask;
    }

    private sealed class ErrorCountingLogger : ILogger<HistoryRecorder>
    {
        public int Errors;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Interlocked.Increment(ref Errors);
            }
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required UpsMonitor Monitor { get; init; }
        public required FakeNutClient Client { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required ShutdownCoordinator Coordinator { get; init; }
        public required FakePowerActions PowerActions { get; init; }
        public required FakeStore Store { get; init; }
        public required HistoryRecorder Recorder { get; init; }
        public required HistorySettings History { get; init; }
        public required EventCollector<UpsReading> Readings { get; init; }
        public required EventCollector<MonitorStateChangedEventArgs> StateChanges { get; init; }
        public required ErrorCountingLogger Logger { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Recorder.DisposeAsync();
            await Coordinator.DisposeAsync();
            await Monitor.DisposeAsync();
        }

        /// <summary>Polls repeatedly, one clock second each.</summary>
        public async Task PollForAsync(int seconds)
        {
            for (var i = 0; i < seconds; i++)
            {
                await PollAsync();
            }
        }

        /// <summary>Reads store events until one of <paramref name="kind"/> arrives; returns everything read, in order.</summary>
        public async Task<List<HistoryEvent>> EventsUntilAsync(HistoryEventKind kind)
        {
            var seen = new List<HistoryEvent>();
            do
            {
                seen.Add(await Store.Events.NextAsync());
            }
            while (seen[^1].Kind != kind);

            return seen;
        }

        /// <summary>One poll: advance the clock a second and wait for the resulting reading.</summary>
        public async Task PollAsync(string? status = null, string? charge = null)
        {
            if (status is not null)
            {
                Client.Variables["ups.status"] = status;
            }

            if (charge is not null)
            {
                Client.Variables["battery.charge"] = charge;
            }

            Time.Advance(TimeSpan.FromSeconds(1));
            await Readings.NextAsync();
        }
    }

    /// <summary>Builds the recorder first and starts the monitor afterwards, as the app does.</summary>
    private static async Task<Harness> CreateHarnessAsync(
        HistorySettings? history = null, Action<FakeStore>? configureStore = null, bool start = true, PowerSettings? power = null)
    {
        var client = new FakeNutClient();
        client.Variables["ups.status"] = "OL";
        client.Variables["battery.charge"] = "80";
        client.Variables["input.voltage"] = "230";

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));
        var monitor = new UpsMonitor(() => client, time, NullLogger<UpsMonitor>.Instance);
        var powerActions = new FakePowerActions();
        var harnessPower = power ?? new PowerSettings { StopImmediately = true, BatteryChargeFloor = 30 };
        var coordinator = new ShutdownCoordinator(
            monitor, powerActions, () => harnessPower, time, NullLogger<ShutdownCoordinator>.Instance)
        {
            DryRun = false,
        };

        var store = new FakeStore();
        configureStore?.Invoke(store);
        var settings = history ?? new HistorySettings();
        var logger = new ErrorCountingLogger();
        var recorder = new HistoryRecorder(monitor, coordinator, store, time, () => settings, logger);

        var readings = new EventCollector<UpsReading>();
        monitor.ReadingUpdated += (_, r) => readings.Add(r);
        var stateChanges = new EventCollector<MonitorStateChangedEventArgs>();
        monitor.StateChanged += (_, e) => stateChanges.Add(e);

        var harness = new Harness
        {
            Monitor = monitor,
            Client = client,
            Time = time,
            Coordinator = coordinator,
            PowerActions = powerActions,
            Store = store,
            Recorder = recorder,
            History = settings,
            Readings = readings,
            StateChanges = stateChanges,
            Logger = logger,
        };

        if (start)
        {
            await harness.Monitor.StartAsync(
                new ConnectionSettings { Host = "nut.example", Port = 3493, UpsName = "ups", PollIntervalMs = 1000 },
                nominalFrequency: 50);
            await readings.NextAsync(); // the initial reading
        }

        return harness;
    }

    private static readonly UpsKey Key = new("nut.example", 3493, "ups");

    [Fact]
    public async Task Startup_OpensTheStore_AndPrunesWithTheConfiguredRetention()
    {
        await using var h = await CreateHarnessAsync(new HistorySettings { RetentionDays = 7 });

        var prune = await h.Store.Prunes.NextAsync();

        Assert.Equal(TimeSpan.FromDays(7), prune.Retention);
        Assert.Equal(1, h.Store.InitializeCalls);
    }

    [Fact]
    public async Task Prune_RunsAgainEveryHour()
    {
        await using var h = await CreateHarnessAsync();
        await h.Store.Prunes.NextAsync();

        h.Time.Advance(HistoryRecorder.PruneInterval);

        await h.Store.Prunes.NextAsync();
    }

    [Fact]
    public async Task Samples_AreThrottledToTheSampleInterval_AndFlushedEveryMinute()
    {
        await using var h = await CreateHarnessAsync(new HistorySettings { SampleIntervalSeconds = 10 });

        // t = 0 was the initial reading; poll once a second up to t = 59, just before the flush timer.
        for (var i = 0; i < 59; i++)
        {
            await h.PollAsync();
        }

        await h.Store.SampleBatches.AssertNoneAsync();

        h.Time.Advance(TimeSpan.FromSeconds(1)); // t = 60: flush timer (and a poll)
        var (ups, samples) = await h.Store.SampleBatches.NextAsync();

        Assert.Equal(Key, ups);
        Assert.InRange(samples.Count, 6, 7);
        var start = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            Enumerable.Range(0, samples.Count).Select(i => start.AddSeconds(10 * i)),
            samples.Select(s => s.Timestamp));
        Assert.All(samples, s =>
        {
            Assert.Equal("OL", s.Status);
            Assert.Equal(80, s.BatteryCharge);
            Assert.Equal(230, s.InputVoltage);
            Assert.False(s.IsAggregated);
        });
    }

    [Fact]
    public async Task Dispose_FlushesBufferedSamples()
    {
        var h = await CreateHarnessAsync();
        await h.PollAsync();

        await h.Recorder.DisposeAsync();

        var (_, samples) = await h.Store.SampleBatches.NextAsync();
        Assert.Single(samples); // only the initial reading; the second is inside the 10 s interval
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_StopsRecording()
    {
        var h = await CreateHarnessAsync();
        await h.Recorder.DisposeAsync();
        await h.Store.SampleBatches.NextAsync();

        await h.PollAsync("OB");

        await h.Store.Events.NextAsync(); // Connected, from before the dispose
        await h.Store.Events.AssertNoneAsync();
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }

    [Fact]
    public async Task StatusChanges_AreRecordedAsEvents_WithChargeAndRuntimeDetail()
    {
        // Floors at 0 so only the OB+LB rule can start a (non-immediate) countdown, which is recorded too.
        await using var h = await CreateHarnessAsync(power: new PowerSettings { StopImmediately = false, BatteryChargeFloor = 0, RuntimeFloorSeconds = 0 });
        var connected = await h.Store.Events.NextAsync();
        Assert.Equal(HistoryEventKind.Connected, connected.Kind);
        Assert.Equal(Key, connected.Ups);

        h.Client.Variables["battery.runtime"] = "600";
        await h.PollAsync("OB", "75");
        var onBattery = await h.Store.Events.NextAsync();
        await h.PollAsync("OB LB", "20");
        var lowBatteryEvents = new[] { await h.Store.Events.NextAsync(), await h.Store.Events.NextAsync() };
        await h.PollAsync("OB LB FSD");
        var fsd = await h.Store.Events.NextAsync();
        await h.PollAsync("OL", "30");
        var onLineEvents = new[] { await h.Store.Events.NextAsync(), await h.Store.Events.NextAsync() };

        Assert.Equal(HistoryEventKind.OnBattery, onBattery.Kind);
        Assert.Equal("status=OB, charge=75%, runtime=600s", onBattery.Detail);
        var lowBattery = Assert.Single(lowBatteryEvents, e => e.Kind == HistoryEventKind.LowBattery);
        Assert.Contains("charge=20%", lowBattery.Detail);
        Assert.Contains(lowBatteryEvents, e => e.Kind == HistoryEventKind.ShutdownPending);
        Assert.Equal(HistoryEventKind.ForcedShutdown, fsd.Kind);
        Assert.Equivalent(
            new[] { HistoryEventKind.OnLine, HistoryEventKind.ShutdownCancelled }, onLineEvents.Select(e => e.Kind));
        Assert.All(
            new[] { onBattery, fsd }.Concat(lowBatteryEvents).Concat(onLineEvents),
            e => Assert.Equal(h.Time.GetUtcNow().Date, e.Timestamp.Date));
    }

    [Fact]
    public async Task ConnectionLoss_IsRecordedWithTheErrorMessage()
    {
        await using var h = await CreateHarnessAsync();
        Assert.Equal(HistoryEventKind.Connected, (await h.Store.Events.NextAsync()).Kind);

        h.Client.ListVarsResults.Enqueue(new IOException("Connection reset by peer."));
        h.Time.Advance(TimeSpan.FromSeconds(1));

        var lost = await h.Store.Events.NextAsync();
        Assert.Equal(HistoryEventKind.ConnectionLost, lost.Kind);
        Assert.Equal("IOException: Connection reset by peer.", lost.Detail);
    }

    [Fact]
    public async Task FailedInitialConnection_IsRecordedAsConnectionLost()
    {
        await using var h = await CreateHarnessAsync(start: false);
        h.Client.ConnectResults.Enqueue(new IOException("No route to host."));

        await h.Monitor.StartAsync(
            new ConnectionSettings { Host = "nut.example", Port = 3493, UpsName = "ups" }, nominalFrequency: 50);

        var lost = await h.Store.Events.NextAsync();
        Assert.Equal(HistoryEventKind.ConnectionLost, lost.Kind);
        Assert.Contains("No route to host.", lost.Detail);
    }

    [Fact]
    public async Task ShutdownCountdown_IsRecorded_PendingThenCancelled()
    {
        await using var h = await CreateHarnessAsync(power: new PowerSettings { StopImmediately = false, StopDelaySeconds = 30, BatteryChargeFloor = 30 });
        await h.Store.Events.NextAsync(); // Connected

        await h.PollAsync("OB", "10");
        var started = new[] { await h.Store.Events.NextAsync(), await h.Store.Events.NextAsync() };
        await h.PollAsync("OL", "80");
        var restored = new[] { await h.Store.Events.NextAsync(), await h.Store.Events.NextAsync() };

        var pending = Assert.Single(started, e => e.Kind == HistoryEventKind.ShutdownPending);
        Assert.Equal("BatteryChargeFloor", pending.Detail);
        Assert.Contains(started, e => e.Kind == HistoryEventKind.OnBattery);
        var cancelled = Assert.Single(restored, e => e.Kind == HistoryEventKind.ShutdownCancelled);
        Assert.Equal("PowerRestored", cancelled.Detail);
        Assert.Contains(restored, e => e.Kind == HistoryEventKind.OnLine);
    }

    [Fact]
    public async Task ShutdownExecuting_FlushesSamplesAndTheEventImmediately()
    {
        await using var h = await CreateHarnessAsync();
        await h.Store.Events.NextAsync(); // Connected

        // Far from the 60 s flush timer: only the shutdown can cause this write.
        await h.PollAsync("OB", "10");

        var (ups, samples) = await h.Store.SampleBatches.NextAsync();
        Assert.Equal(Key, ups);
        // The initial reading and the one that triggered the stop, although it is inside the sample interval.
        Assert.Equal(["OL", "OB"], samples.Select(s => s.Status));
        var events = await h.EventsUntilAsync(HistoryEventKind.ShutdownExecuting);
        Assert.Equal("BatteryChargeFloor", events[^1].Detail);
    }

    [Fact]
    public async Task ShutdownFailure_IsRecordedWithTheExceptionMessage()
    {
        await using var h = await CreateHarnessAsync();
        h.PowerActions.ThrowOnExecute = new InvalidOperationException("denied");
        await h.Store.Events.NextAsync(); // Connected

        await h.PollAsync("OB", "10");

        var events = await h.EventsUntilAsync(HistoryEventKind.ShutdownFailed);
        Assert.Contains(events, e => e.Kind == HistoryEventKind.ShutdownExecuting);
        Assert.Equal("InvalidOperationException: denied", events[^1].Detail);
    }

    [Fact]
    public async Task WhenDisabled_NothingIsRecorded()
    {
        var h = await CreateHarnessAsync(new HistorySettings { Enabled = false });

        await h.PollAsync("OB", "75");
        await h.PollForAsync(60);
        await h.Recorder.DisposeAsync();

        await h.Store.SampleBatches.AssertNoneAsync();
        await h.Store.Events.AssertNoneAsync();
        await h.Store.Prunes.AssertNoneAsync();
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }

    [Fact]
    public async Task TogglingEnabledAtRuntime_TakesEffectImmediately()
    {
        var h = await CreateHarnessAsync(new HistorySettings { Enabled = false });
        await h.PollAsync();

        h.History.Enabled = true;
        await h.PollAsync("OB", "75");
        var onBattery = await h.Store.Events.NextAsync();
        h.History.Enabled = false;
        await h.PollAsync("OL");
        await h.Recorder.DisposeAsync();

        Assert.Equal(HistoryEventKind.OnBattery, onBattery.Kind);
        await h.Store.Events.AssertNoneAsync(); // the OnLine event happened while disabled
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }

    [Fact]
    public async Task StoreErrors_AreSwallowed_AndTheMonitorKeepsRunning()
    {
        var h = await CreateHarnessAsync(configureStore: s => s.AppendError = new IOException("disk full"));

        await h.PollAsync("OB", "75"); // event write fails
        await h.PollForAsync(60); // sample flush fails
        await h.Recorder.DisposeAsync();

        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        Assert.Equal(0, h.Logger.Errors);
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }

    [Fact]
    public async Task UnopenableDatabase_DisablesTheRecorder_WithASingleErrorLog()
    {
        var h = await CreateHarnessAsync(configureStore: s => s.InitializeError = new IOException("unable to open database file"));

        await h.PollAsync("OB", "75");
        await h.PollForAsync(60);
        await h.Recorder.DisposeAsync();

        Assert.Equal(1, h.Logger.Errors);
        await h.Store.SampleBatches.AssertNoneAsync();
        await h.Store.Events.AssertNoneAsync();
        await h.Store.Prunes.AssertNoneAsync();
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        await h.Coordinator.DisposeAsync();
        await h.Monitor.DisposeAsync();
    }
}
