using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Tests.Fakes;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Tests.Monitoring;

/// <summary>
/// Exercises <see cref="UpsMonitor"/> against the real <see cref="NutClient"/> (WP-A) and
/// <see cref="FakeNutServer"/> instead of <see cref="FakeNutClient"/>, to cross-check the assumptions
/// <see cref="FakeNutClient"/> makes about <see cref="NutClient"/>'s error contract (transport failures throw
/// <see cref="IOException"/>/<see cref="System.Net.Sockets.SocketException"/> and raise
/// <see cref="INutClient.ConnectionLost"/> once; server <c>ERR</c> replies throw <see cref="NutException"/> and
/// leave the connection usable). Uses <see cref="TimeProvider.System"/> with short poll intervals over a real
/// loopback TCP connection; each test completes in well under 2 seconds.
/// </summary>
public class UpsMonitorIntegrationTests
{
    private const string UpsName = "ups1";

    private static Func<string, IEnumerable<string>> BuildHandler(
        Dictionary<string, string> vars,
        Func<string, IEnumerable<string>?>? overrides = null)
    {
        return line =>
        {
            var overridden = overrides?.Invoke(line);
            if (overridden is not null)
            {
                return overridden;
            }

            if (line == "VER")
            {
                return ["Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/"];
            }

            if (line == "NETVER")
            {
                return ["1.3"];
            }

            if (line == $"LOGIN {UpsName}")
            {
                return ["OK"];
            }

            if (line == $"LIST VAR {UpsName}")
            {
                var response = new List<string> { $"BEGIN LIST VAR {UpsName}" };
                foreach (var (name, value) in vars)
                {
                    response.Add($"VAR {UpsName} {name} \"{value}\"");
                }

                response.Add($"END LIST VAR {UpsName}");
                return response;
            }

            return ["ERR UNKNOWN-COMMAND"];
        };
    }

    private static Dictionary<string, string> DefaultVars() => new()
    {
        ["ups.status"] = "OL",
        ["battery.charge"] = "80",
        ["battery.voltage"] = "24",
        ["battery.runtime"] = "3600",
        ["input.voltage"] = "230",
        ["input.frequency"] = "50",
        ["output.voltage"] = "230",
        ["ups.load"] = "40",
        ["ups.mfr"] = "Acme",
    };

    private static ConnectionSettings NewSettings(int port, bool autoReconnect = false) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        UpsName = UpsName,
        PollIntervalMs = 100,
        AutoReconnect = autoReconnect,
    };

    private static UpsMonitor NewMonitor() => new(() => new NutClient(), TimeProvider.System, NullLogger<UpsMonitor>.Instance);

    [Fact]
    public async Task Connect_LoginAndFirstPoll_DeliversAReadingWithTheServersValues()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        server.SetHandler(BuildHandler(DefaultVars()));

        await using var monitor = NewMonitor();
        var readings = new EventCollector<UpsReading>();
        monitor.ReadingUpdated += (_, r) => readings.Add(r);

        await monitor.StartAsync(NewSettings(server.Port), nominalFrequency: 60);

        Assert.Equal(MonitorState.Connected, monitor.State);
        var reading = await readings.NextAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.Equal(80, reading.BatteryCharge);
        Assert.False(reading.BatteryChargeEstimated);
        Assert.Equal(24, reading.BatteryVoltage);
        Assert.Equal(TimeSpan.FromSeconds(3600), reading.BatteryRuntime);
        Assert.Equal(230, reading.InputVoltage);
        Assert.Equal(50, reading.InputFrequency);
        Assert.Equal(40, reading.LoadPercent);
        Assert.Equal("Acme", monitor.Info?.Manufacturer);
        Assert.Equal("Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/", monitor.ServerVersion);
        Assert.Equal("1.3", monitor.ProtocolVersion);
    }

    [Fact]
    public async Task ServerDropsConnection_RaisesConnectionLostAndReconnects_WithAutoReconnect()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        server.SetHandler(BuildHandler(DefaultVars()));

        await using var monitor = NewMonitor();
        var readings = new EventCollector<UpsReading>();
        var connectionLosses = new EventCollector<Exception?>();
        var stateChanges = new EventCollector<MonitorStateChangedEventArgs>();
        monitor.ReadingUpdated += (_, r) => readings.Add(r);
        monitor.ConnectionLost += (_, ex) => connectionLosses.Add(ex);
        monitor.StateChanged += (_, e) => stateChanges.Add(e);

        await monitor.StartAsync(NewSettings(server.Port, autoReconnect: true), nominalFrequency: 60);
        await readings.NextAsync(TimeSpan.FromSeconds(2)); // first, pre-drop reading.
        await stateChanges.NextAsync(TimeSpan.FromSeconds(2)); // Disconnected -> Connecting
        await stateChanges.NextAsync(TimeSpan.FromSeconds(2)); // Connecting -> Connected

        server.DropConnection();

        var lost = await connectionLosses.NextAsync(TimeSpan.FromSeconds(2));
        Assert.True(lost is IOException or System.Net.Sockets.SocketException);

        var reconnecting = await stateChanges.NextAsync(TimeSpan.FromSeconds(2)); // Connected -> Reconnecting
        Assert.Equal(MonitorState.Reconnecting, reconnecting.NewState);
        var reconnected = await stateChanges.NextAsync(TimeSpan.FromSeconds(2)); // Reconnecting -> Connected
        Assert.Equal(MonitorState.Connected, reconnected.NewState);

        var readingAfterReconnect = await readings.NextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(UpsStatus.OL, readingAfterReconnect.Status);
        Assert.Equal(MonitorState.Connected, monitor.State);
    }

    [Fact]
    public async Task LoginRejectedWithAccessDenied_GoesDisconnected_WithoutReconnecting_EvenWithAutoReconnect()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        server.SetHandler(BuildHandler(
            DefaultVars(),
            overrides: line => line == $"LOGIN {UpsName}" ? ["ERR ACCESS-DENIED"] : null));

        await using var monitor = NewMonitor();
        var stateChanges = new EventCollector<MonitorStateChangedEventArgs>();
        var reconnectAttempts = new EventCollector<int>();
        monitor.StateChanged += (_, e) => stateChanges.Add(e);
        monitor.ReconnectAttempt += (_, a) => reconnectAttempts.Add(a);

        await monitor.StartAsync(NewSettings(server.Port, autoReconnect: true), nominalFrequency: 60);

        var connecting = await stateChanges.NextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MonitorState.Connecting, connecting.NewState);
        var disconnected = await stateChanges.NextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(MonitorState.Disconnected, disconnected.NewState);
        var error = Assert.IsType<NutException>(disconnected.Error);
        Assert.Equal(NutErrorCode.AccessDenied, error.ErrorCode);

        Assert.Equal(MonitorState.Disconnected, monitor.State);
        await stateChanges.AssertNoneAsync(TimeSpan.FromMilliseconds(300));
        await reconnectAttempts.AssertNoneAsync(TimeSpan.FromMilliseconds(300));
        Assert.Single(server.ReceivedLines, l => l == $"LOGIN {UpsName}");
    }

    [Fact]
    public async Task DataStaleOnce_ThenOk_StillDeliversTheReading()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        var listVarCalls = 0;
        server.SetHandler(BuildHandler(
            DefaultVars(),
            overrides: line =>
            {
                if (line != $"LIST VAR {UpsName}")
                {
                    return null;
                }

                listVarCalls++;
                return listVarCalls == 1 ? ["ERR DATA-STALE"] : null; // null falls through to the normal handler.
            }));

        await using var monitor = NewMonitor();
        var readings = new EventCollector<UpsReading>();
        monitor.ReadingUpdated += (_, r) => readings.Add(r);

        await monitor.StartAsync(NewSettings(server.Port), nominalFrequency: 60);

        var reading = await readings.NextAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(UpsStatus.OL, reading.Status);
        Assert.True(listVarCalls >= 2);
        Assert.Equal(MonitorState.Connected, monitor.State);
    }
}
