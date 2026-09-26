using CocoNut.Core.Abstractions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Tests.Fakes;
using CocoNut.Core.Tests.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Shutdown;

public class ShutdownCoordinatorTests
{
    private sealed class FakePowerActions : IPowerActions
    {
        public EventCollector<StopAction> Executions { get; } = new();

        public bool IsSupported(StopAction action) => true;

        public Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
        {
            Executions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableSettings
    {
        public PowerSettings Value { get; set; } = new();
    }

    private sealed record Harness(
        UpsMonitor Monitor,
        FakeNutClient Client,
        FakeTimeProvider TimeProvider,
        ShutdownCoordinator Coordinator,
        FakePowerActions PowerActions,
        MutableSettings Settings,
        EventCollector<ShutdownPendingEventArgs> Pending,
        EventCollector<ShutdownReason> Cancelled,
        EventCollector<ShutdownReason> Executing) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            await Monitor.DisposeAsync();
        }
    }

    private static async Task<Harness> CreateStartedHarnessAsync(PowerSettings? settings = null)
    {
        var client = new FakeNutClient();
        client.Variables["ups.status"] = "OL";
        client.Variables["battery.charge"] = "80";

        var timeProvider = new FakeTimeProvider();
        var monitor = new UpsMonitor(() => client, timeProvider, NullLogger<UpsMonitor>.Instance);
        var powerActions = new FakePowerActions();
        var mutableSettings = new MutableSettings { Value = settings ?? new PowerSettings() };

        var coordinator = new ShutdownCoordinator(
            monitor, powerActions, () => mutableSettings.Value, timeProvider, NullLogger<ShutdownCoordinator>.Instance)
        {
            DryRun = false,
        };

        var pending = new EventCollector<ShutdownPendingEventArgs>();
        var cancelled = new EventCollector<ShutdownReason>();
        var executing = new EventCollector<ShutdownReason>();
        coordinator.ShutdownPending += (_, e) => pending.Add(e);
        coordinator.ShutdownCancelled += (_, r) => cancelled.Add(r);
        coordinator.ShutdownExecuting += (_, r) => executing.Add(r);

        var readings = new EventCollector<int>();
        monitor.ReadingUpdated += (_, _) => readings.Add(0);

        await monitor.StartAsync(new ConnectionSettings { PollIntervalMs = 1000, AutoReconnect = false }, nominalFrequency: 50);
        await readings.NextAsync(); // initial OL reading, consumed so later NextAsync calls line up with later polls.

        return new Harness(monitor, client, timeProvider, coordinator, powerActions, mutableSettings, pending, cancelled, executing);
    }

    private static async Task PushReadingAsync(Harness h, string status, string? batteryCharge = null)
    {
        h.Client.Variables["ups.status"] = status;
        if (batteryCharge is not null)
        {
            h.Client.Variables["battery.charge"] = batteryCharge;
        }

        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ImmediateMode_BelowChargeFloor_CallsPowerActionOnce()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");

        var action = await h.PowerActions.Executions.NextAsync();
        Assert.Equal(StopAction.Shutdown, action);

        await h.Executing.NextAsync();
        Assert.Equal(MonitorState.Disconnected, h.Monitor.State);
        await h.PowerActions.Executions.AssertNoneAsync(); // executed exactly once.
    }

    [Fact]
    public async Task CountdownMode_RaisesPending_ThenExecutesWhenCountdownCompletes()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 5,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");

        var pending = await h.Pending.NextAsync();
        Assert.Equal(ShutdownReason.BatteryChargeFloor, pending.Reason);
        Assert.True(pending.Countdown.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(5), pending.Countdown.Total);

        await h.PowerActions.Executions.AssertNoneAsync(); // not yet - still counting down.

        h.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        var action = await h.PowerActions.Executions.NextAsync();
        Assert.Equal(StopAction.Shutdown, action);
        await h.Executing.NextAsync();
    }

    [Fact]
    public async Task PowerRestored_CancelsPendingCountdown_AndNeverExecutes()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 10,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        await PushReadingAsync(h, "OL", "80");
        var cancelledReason = await h.Cancelled.NextAsync();
        Assert.Equal(ShutdownReason.PowerRestored, cancelledReason);

        // Even past the original delay, nothing should execute.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(15));
        await h.PowerActions.Executions.AssertNoneAsync();
        Assert.False(h.Coordinator.IsShutdownPending);
    }

    [Fact]
    public async Task DryRun_DoesNotCallThePowerAction_ButStillStopsTheMonitor()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
        });
        h.Coordinator.DryRun = true;

        await PushReadingAsync(h, "OB", "10");

        await h.Executing.NextAsync();
        await h.PowerActions.Executions.AssertNoneAsync();
        Assert.Equal(MonitorState.Disconnected, h.Monitor.State);
    }

    [Fact]
    public async Task ExecuteNowAsync_SkipsTheRestOfTheCountdown()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        var pending = await h.Pending.NextAsync();

        await h.Coordinator.ExecuteNowAsync();

        var action = await h.PowerActions.Executions.NextAsync();
        Assert.Equal(StopAction.Shutdown, action);
        Assert.False(pending.Countdown.IsRunning);

        // The countdown's own timer must not fire a second execution once its original delay would have elapsed.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(30));
        await h.PowerActions.Executions.AssertNoneAsync();
    }

    [Fact]
    public async Task ExecuteNowAsync_CalledTwice_ExecutesOnlyOnce()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        await h.Coordinator.ExecuteNowAsync();
        await h.PowerActions.Executions.NextAsync();
        await h.Executing.NextAsync();

        await h.Coordinator.ExecuteNowAsync();
        await h.PowerActions.Executions.AssertNoneAsync();
        await h.Executing.AssertNoneAsync();
    }

    [Fact]
    public async Task Start_WhileAlreadyPending_DoesNotStartASecondCountdown()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
            RuntimeFloorSeconds = 120,
        });

        await PushReadingAsync(h, "OB", "10");
        var firstPending = await h.Pending.NextAsync();

        // Still on battery, still below the floor on the next poll: must not raise ShutdownPending again.
        await PushReadingAsync(h, "OB", "5");
        await h.Pending.AssertNoneAsync();
        Assert.Same(firstPending.Countdown, h.Coordinator.Countdown);
    }

    [Fact]
    public async Task TryExtend_WhenAllowed_ExtendsTheCountdown()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 5,
            BatteryChargeFloor = 30,
            AllowExtendDelay = true,
            ExtendDelaySeconds = 10,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        Assert.True(h.Coordinator.TryExtend());

        h.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        await h.PowerActions.Executions.AssertNoneAsync(); // extended past the original 5s delay.

        h.TimeProvider.Advance(TimeSpan.FromSeconds(10));
        await h.PowerActions.Executions.NextAsync();
    }

    [Fact]
    public async Task TryExtend_WhenNotAllowedBySettings_DoesNothing()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 5,
            BatteryChargeFloor = 30,
            AllowExtendDelay = false,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        Assert.False(h.Coordinator.TryExtend());
    }
}
