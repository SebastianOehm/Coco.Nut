using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Tests.Fakes;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Monitoring;

public class UpsMonitorTests
{
    private static ConnectionSettings NewSettings(bool autoReconnect = false, int pollIntervalMs = 1000) => new()
    {
        Host = "nut.example",
        Port = 3493,
        UpsName = "ups",
        PollIntervalMs = pollIntervalMs,
        AutoReconnect = autoReconnect,
    };

    private static FakeNutClient NewClient()
    {
        var client = new FakeNutClient();
        client.Variables["ups.status"] = "OL";
        client.Variables["battery.charge"] = "80";
        client.Variables["battery.voltage"] = "24";
        client.Variables["input.voltage"] = "230";
        client.Variables["output.voltage"] = "230";
        client.Variables["ups.load"] = "40";
        client.Variables["ups.mfr"] = "Acme";
        return client;
    }

    private sealed record Harness(UpsMonitor Monitor, FakeNutClient Client, FakeTimeProvider TimeProvider,
        EventCollector<UpsReading> Readings, EventCollector<UpsStatusChangedEventArgs> StatusChanges,
        EventCollector<MonitorStateChangedEventArgs> StateChanges, EventCollector<int> ReconnectAttempts,
        EventCollector<Exception?> ConnectionLosses) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Monitor.DisposeAsync();
    }

    private static Harness CreateHarness(FakeNutClient? client = null)
    {
        client ??= NewClient();
        var timeProvider = new FakeTimeProvider();
        var monitor = new UpsMonitor(() => client, timeProvider, NullLogger<UpsMonitor>.Instance);

        var readings = new EventCollector<UpsReading>();
        var statusChanges = new EventCollector<UpsStatusChangedEventArgs>();
        var stateChanges = new EventCollector<MonitorStateChangedEventArgs>();
        var reconnectAttempts = new EventCollector<int>();
        var connectionLosses = new EventCollector<Exception?>();

        // Subscribed before StartAsync: with a fake client every step completes synchronously, so the very first
        // poll's events are raised nested inside the StartAsync call itself.
        monitor.ReadingUpdated += (_, r) => readings.Add(r);
        monitor.StatusChanged += (_, e) => statusChanges.Add(e);
        monitor.StateChanged += (_, e) => stateChanges.Add(e);
        monitor.ReconnectAttempt += (_, a) => reconnectAttempts.Add(a);
        monitor.ConnectionLost += (_, ex) => connectionLosses.Add(ex);

        return new Harness(monitor, client, timeProvider, readings, statusChanges, stateChanges, reconnectAttempts, connectionLosses);
    }

    [Fact]
    public async Task StartAsync_HappyPath_ConnectsAndDeliversFirstReadingImmediately()
    {
        await using var h = CreateHarness();

        await h.Monitor.StartAsync(NewSettings(), nominalFrequency: 50);

        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal("Acme", h.Monitor.Info?.Manufacturer);

        var change = await h.StatusChanges.NextAsync();
        Assert.Equal(UpsStatus.None, change.Previous);
        Assert.Equal(UpsStatus.OL, change.Current);
        Assert.Equal(UpsStatus.OL, change.NewlyActive);
        Assert.Equal(UpsStatus.None, change.NewlyCleared);
    }

    [Fact]
    public async Task Poll_RepeatsAtConfiguredInterval()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync(); // the immediate first reading

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        var second = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, second.Status);

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        var third = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, third.Status);
    }

    [Fact]
    public async Task StatusChanged_OnlyRaisedWhenStatusActuallyChanges_WithCorrectNewlyActiveAndCleared()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();
        await h.StatusChanges.NextAsync(); // None -> OL

        // Unchanged status: a new reading arrives, but no StatusChanged.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await h.Readings.NextAsync();
        await h.StatusChanges.AssertNoneAsync();

        // Status changes to OB + LB.
        h.Client.Variables["ups.status"] = "OB LB";
        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await h.Readings.NextAsync();
        var change = await h.StatusChanges.NextAsync();
        Assert.Equal(UpsStatus.OL, change.Previous);
        Assert.Equal(UpsStatus.OB | UpsStatus.LB, change.Current);
        Assert.Equal(UpsStatus.OB | UpsStatus.LB, change.NewlyActive);
        Assert.Equal(UpsStatus.OL, change.NewlyCleared);
    }

    [Fact]
    public async Task Poll_DataStaleRetriesThenSucceeds()
    {
        await using var h = CreateHarness();
        h.Client.ListVarsResults.Enqueue(new NutException(NutErrorCode.DataStale, "LIST VAR ups", "ERR DATA-STALE"));
        h.Client.ListVarsResults.Enqueue(new NutException(NutErrorCode.DataStale, "LIST VAR ups", "ERR DATA-STALE"));
        h.Client.ListVarsResults.Enqueue(new NutException(NutErrorCode.DataStale, "LIST VAR ups", "ERR DATA-STALE"));

        await h.Monitor.StartAsync(NewSettings(), nominalFrequency: 50);

        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(4, h.Client.ListVarsCalls); // 3 failures + 1 success, within a single poll.
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
    }

    [Fact]
    public async Task Poll_DataStaleExhausted_SkipsPollButKeepsConnection()
    {
        await using var h = CreateHarness();
        for (var i = 0; i < 4; i++)
        {
            h.Client.ListVarsResults.Enqueue(new NutException(NutErrorCode.DataStale, "LIST VAR ups", "ERR DATA-STALE"));
        }

        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);

        await h.Readings.AssertNoneAsync();
        Assert.Equal(4, h.Client.ListVarsCalls);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);

        // The connection was kept; the next poll succeeds normally.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
    }

    [Fact]
    public async Task Poll_DriverNotConnected_SkipsPollButKeepsConnection()
    {
        await using var h = CreateHarness();
        h.Client.ListVarsResults.Enqueue(new NutException(NutErrorCode.DriverNotConnected, "LIST VAR ups", "ERR DRIVER-NOT-CONNECTED"));

        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);

        await h.Readings.AssertNoneAsync();
        Assert.Equal(MonitorState.Connected, h.Monitor.State);

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await h.Readings.NextAsync();
    }

    [Fact]
    public async Task StartAsync_LoginFailure_TransitionsToDisconnected_AndDoesNotReconnectEvenWhenAutoReconnectIsSet()
    {
        await using var h = CreateHarness();
        h.Client.LoginResults.Enqueue(new NutException(NutErrorCode.AccessDenied, "LOGIN ups", "ERR ACCESS-DENIED"));

        await h.Monitor.StartAsync(NewSettings(autoReconnect: true), nominalFrequency: 50);

        Assert.Equal(MonitorState.Disconnected, h.Monitor.State);
        Assert.Equal(1, h.Client.ConnectCalls);
        Assert.Equal(1, h.Client.LoginCalls);

        var change = await h.StateChanges.NextAsync(); // Disconnected -> Connecting
        Assert.Equal(MonitorState.Connecting, change.NewState);
        change = await h.StateChanges.NextAsync(); // Connecting -> Disconnected (fatal)
        Assert.Equal(MonitorState.Disconnected, change.NewState);
        Assert.IsType<NutException>(change.Error);
        Assert.Equal(NutErrorCode.AccessDenied, ((NutException)change.Error!).ErrorCode);

        await h.StateChanges.AssertNoneAsync();
        await h.ReconnectAttempts.AssertNoneAsync();
    }

    [Fact]
    public async Task ConnectionLost_WithAutoReconnect_Reconnects()
    {
        await using var h = CreateHarness();
        h.Client.ListVarsResults.Enqueue(new IOException("Connection reset by peer."));

        await h.Monitor.StartAsync(NewSettings(autoReconnect: true), nominalFrequency: 50);

        var lost = await h.ConnectionLosses.NextAsync();
        Assert.IsType<IOException>(lost);

        var attempt = await h.ReconnectAttempts.NextAsync();
        Assert.Equal(1, attempt);

        // Reconnected and polling again.
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
    }

    [Fact]
    public async Task ConnectionLost_WithoutAutoReconnect_StaysDisconnected()
    {
        await using var h = CreateHarness();
        h.Client.ListVarsResults.Enqueue(new IOException("Connection reset by peer."));

        await h.Monitor.StartAsync(NewSettings(autoReconnect: false), nominalFrequency: 50);

        var lost = await h.ConnectionLosses.NextAsync();
        Assert.IsType<IOException>(lost);

        Assert.Equal(MonitorState.Disconnected, h.Monitor.State);
        Assert.Equal(1, h.Client.ConnectCalls);
        await h.ReconnectAttempts.AssertNoneAsync();
    }

    [Fact]
    public async Task Reconnect_BackoffDelayDoublesBetweenFailedAttempts()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(autoReconnect: true, pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();

        h.Client.ListVarsResults.Enqueue(new IOException("boom"));
        h.Client.ConnectResults.Enqueue(new System.Net.Sockets.SocketException()); // first reconnect attempt fails

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1)); // triggers the failing poll -> ConnectionLost -> Reconnecting
        await h.ConnectionLosses.NextAsync();
        Assert.Equal(1, await h.ReconnectAttempts.NextAsync());

        // The failed reconnect attempt waits 5s (initial delay) before retrying; nothing happens well before that.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await h.ReconnectAttempts.AssertNoneAsync(TimeSpan.FromMilliseconds(50));

        // Completes the 5s backoff. Task.Delay(5s,...) is registered reactively (after ConnectOnceAsync's failure
        // is handled), so under heavy load a single Advance() can race its registration - retry via the
        // FakeTimeProvider overload instead of a single bare Advance() + NextAsync().
        Assert.Equal(2, await h.ReconnectAttempts.NextAsync(h.TimeProvider, TimeSpan.FromSeconds(2)));

        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
    }

    [Fact]
    public async Task StopAsync_StopsTheLoop_AndRaisesNoFurtherEvents()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();
        await h.StateChanges.NextAsync(); // Disconnected -> Connecting
        await h.StateChanges.NextAsync(); // Connecting -> Connected

        await h.Monitor.StopAsync();
        await h.StateChanges.NextAsync(); // Connected -> Disconnected

        Assert.Equal(MonitorState.Disconnected, h.Monitor.State);
        Assert.Equal(1, h.Client.DisconnectCalls);

        h.TimeProvider.Advance(TimeSpan.FromSeconds(10));
        await h.Readings.AssertNoneAsync();
        await h.StateChanges.AssertNoneAsync();
    }

    [Fact]
    public async Task TimeGap_ImmediateReconnectSucceeds_ResumesPolling_WithoutRaisingConnectionLostOrReconnectAttempt()
    {
        // The forced attempt is not part of the AutoReconnect backoff loop, so no ReconnectAttempt is raised for
        // it, and since nothing actually broke, ConnectionLost is not raised either (see UpsMonitor.ExitPollingAsync).
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(autoReconnect: true, pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();

        // gapThreshold = max(3 * 1s, 30s) = 30s.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(45));

        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);

        await h.ConnectionLosses.AssertNoneAsync();
        await h.ReconnectAttempts.AssertNoneAsync();
    }

    [Fact]
    public async Task TimeGap_WithoutAutoReconnect_StillMakesOneReconnectAttempt_AndSucceeds()
    {
        // Unlike an ordinary transport failure, a detected resume-from-sleep gap always gets one immediate
        // reconnect attempt, even when AutoReconnect is off - leaving the user unprotected after every resume
        // would be worse than WinNUT's behaviour (which always reconnected on PowerModes.Resume).
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(autoReconnect: false, pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();

        h.TimeProvider.Advance(TimeSpan.FromSeconds(45));

        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        await h.ConnectionLosses.AssertNoneAsync();
    }

    [Fact]
    public async Task TimeGap_WithoutAutoReconnect_ForcedAttemptFails_GoesDisconnected_WithNoFurtherRetries()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(autoReconnect: false, pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();
        await h.StateChanges.NextAsync(); // Disconnected -> Connecting
        await h.StateChanges.NextAsync(); // Connecting -> Connected

        h.Client.ConnectResults.Enqueue(new System.Net.Sockets.SocketException());
        h.TimeProvider.Advance(TimeSpan.FromSeconds(45));

        var change = await h.StateChanges.NextAsync(); // Connected -> Reconnecting (the forced attempt starting)
        Assert.Equal(MonitorState.Reconnecting, change.NewState);
        change = await h.StateChanges.NextAsync(); // Reconnecting -> Disconnected (the forced attempt failed)
        Assert.Equal(MonitorState.Disconnected, change.NewState);
        Assert.IsType<System.Net.Sockets.SocketException>(change.Error);

        await h.StateChanges.AssertNoneAsync();
        await h.ReconnectAttempts.AssertNoneAsync();
        await h.ConnectionLosses.AssertNoneAsync();
    }

    [Fact]
    public async Task TimeGap_WithAutoReconnect_ForcedAttemptFails_FallsBackToTheNormalBackoffLoop()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(autoReconnect: true, pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();

        h.Client.ConnectResults.Enqueue(new System.Net.Sockets.SocketException()); // the forced gap attempt fails
        h.TimeProvider.Advance(TimeSpan.FromSeconds(45));

        // Falls back into the normal backoff loop, which succeeds on its first (real) attempt.
        Assert.Equal(1, await h.ReconnectAttempts.NextAsync());
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
    }

    [Fact]
    public async Task EventHandlerException_IsCaughtAndDoesNotStopTheLoop()
    {
        await using var h = CreateHarness();
        var goodReadings = new EventCollector<UpsReading>();
        h.Monitor.ReadingUpdated += (_, _) => throw new InvalidOperationException("Misbehaving handler.");
        h.Monitor.ReadingUpdated += (_, r) => goodReadings.Add(r);

        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);
        await goodReadings.NextAsync();

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await goodReadings.NextAsync();

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await goodReadings.NextAsync();

        Assert.Equal(MonitorState.Connected, h.Monitor.State);
    }

    [Fact]
    public async Task GetAllVariablesAsync_ReturnsVariablesWithDescriptionsWhenAvailable()
    {
        await using var h = CreateHarness();
        h.Client.Descriptions["ups.status"] = "UPS status";
        await h.Monitor.StartAsync(NewSettings(), nominalFrequency: 50);
        await h.Readings.NextAsync();

        var vars = await h.Monitor.GetAllVariablesAsync(includeDescriptions: true);

        var status = Assert.Single(vars, v => v.Name == "ups.status");
        Assert.Equal("UPS status", status.Description);
        var mfr = Assert.Single(vars, v => v.Name == "ups.mfr");
        Assert.Null(mfr.Description); // no description configured for this one; tolerated.
    }

    [Fact]
    public async Task RestartAsync_UsesPreviousSettingsAndFrequency()
    {
        await using var h = CreateHarness();
        await h.Monitor.StartAsync(NewSettings(pollIntervalMs: 1000), nominalFrequency: 50);
        await h.Readings.NextAsync();

        await h.Monitor.RestartAsync();

        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        var reading = await h.Readings.NextAsync();
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(2, h.Client.ConnectCalls);
    }
}
