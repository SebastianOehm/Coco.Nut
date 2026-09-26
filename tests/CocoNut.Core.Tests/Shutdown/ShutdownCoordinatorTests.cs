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

        /// <summary>When set, the next <see cref="ExecuteAsync"/> call throws this instead of "succeeding", then
        /// clears itself so a later call succeeds normally.</summary>
        public Exception? ThrowOnExecute { get; set; }

        public bool IsSupported(StopAction action) => true;

        public Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
        {
            var toThrow = ThrowOnExecute;
            if (toThrow is not null)
            {
                ThrowOnExecute = null;
                throw toThrow;
            }

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
        EventCollector<ShutdownReason> Executing,
        EventCollector<Exception> Failed,
        EventCollector<int> Rearmed,
        EventCollector<MonitorStateChangedEventArgs> StateChanges) : IAsyncDisposable
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
        var failed = new EventCollector<Exception>();
        var rearmed = new EventCollector<int>();
        coordinator.ShutdownPending += (_, e) => pending.Add(e);
        coordinator.ShutdownCancelled += (_, r) => cancelled.Add(r);
        coordinator.ShutdownExecuting += (_, r) => executing.Add(r);
        coordinator.ShutdownFailed += (_, ex) => failed.Add(ex);
        coordinator.Rearmed += (_, _) => rearmed.Add(0);

        var readings = new EventCollector<int>();
        monitor.ReadingUpdated += (_, _) => readings.Add(0);
        var stateChanges = new EventCollector<MonitorStateChangedEventArgs>();
        monitor.StateChanged += (_, e) => stateChanges.Add(e);

        await monitor.StartAsync(new ConnectionSettings { PollIntervalMs = 1000, AutoReconnect = false }, nominalFrequency: 50);
        await readings.NextAsync(); // initial OL reading, consumed so later NextAsync calls line up with later polls.
        await stateChanges.NextAsync(); // Disconnected -> Connecting
        await stateChanges.NextAsync(); // Connecting -> Connected

        return new Harness(monitor, client, timeProvider, coordinator, powerActions, mutableSettings, pending, cancelled, executing, failed, rearmed, stateChanges);
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

    /// <summary>
    /// Waits for the three <see cref="MonitorState"/> transitions a restart produces (Connected -> Disconnected via
    /// the LOGOUT before the action, then Disconnected -> Connecting -> Connected via <see cref="UpsMonitor.RestartAsync"/>
    /// after it), which is how the tests confirm <see cref="ShutdownCoordinator"/> restarted the monitor.
    /// </summary>
    private static async Task WaitForRestartAsync(Harness h)
    {
        await h.StateChanges.NextAsync(); // Connected -> Disconnected (StopAsync, before the action)
        await h.StateChanges.NextAsync(); // Disconnected -> Connecting (RestartAsync, after the action)
        await h.StateChanges.NextAsync(); // Connecting -> Connected
    }

    [Fact]
    public async Task ImmediateMode_BelowChargeFloor_CallsPowerActionOnce_ThenRestartsTheMonitor()
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

        await WaitForRestartAsync(h);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        await h.PowerActions.Executions.AssertNoneAsync(); // executed exactly once so far.
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
    public async Task CancelPending_IgnoredOnceExecutionHasStarted_EvenFromTheRestartsOwnFirstPoll()
    {
        // Regression coverage for the case that motivated guarding CancelPending on _executed: without it, the
        // monitor restart's own immediate first poll (run synchronously as part of RestartAsync, before ExecuteAsync
        // resets its state) could observe "back online" and cancel a shutdown that has already run.
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        // By the time the monitor restarts (as part of executing), mains power is back - the restart's own first
        // poll observes this, but must not be allowed to cancel an execution that has already started.
        h.Client.Variables["ups.status"] = "OL";
        h.Client.Variables["battery.charge"] = "80";

        await h.Coordinator.ExecuteNowAsync();

        await h.PowerActions.Executions.NextAsync();
        await h.Cancelled.AssertNoneAsync();
        Assert.False(h.Coordinator.IsShutdownPending);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
    }

    [Fact]
    public async Task DryRun_DoesNotCallThePowerAction_ButStillStopsAndRestartsTheMonitor()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
        });
        h.Coordinator.DryRun = true;

        await PushReadingAsync(h, "OB", "10");

        await h.Executing.NextAsync();
        await WaitForRestartAsync(h);
        await h.PowerActions.Executions.AssertNoneAsync();
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
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
    public async Task ExecuteNowAsync_ConcurrentCalls_ExecuteOnlyOnce()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        // _executed is set synchronously as the very first step of ExecuteAsync, before either call reaches a real
        // await, so calling this twice without awaiting in between still deterministically exercises the guard.
        var first = h.Coordinator.ExecuteNowAsync();
        var second = h.Coordinator.ExecuteNowAsync();
        await Task.WhenAll(first, second);

        await h.PowerActions.Executions.NextAsync();
        await h.PowerActions.Executions.AssertNoneAsync();
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

    [Fact]
    public async Task Rearm_AfterSuccessfulShutdownAction_UsesFiveMinuteDelay()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
            StopAction = StopAction.Shutdown,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.PowerActions.Executions.NextAsync();
        await WaitForRestartAsync(h);

        // Not yet - a successful Shutdown re-arms after 5 minutes, not 60 seconds.
        h.TimeProvider.Advance(ShutdownCoordinator.DefaultRearmDelay);
        await h.Rearmed.AssertNoneAsync();
        // Still critical (unchanged OB/low charge) but disarmed: must not have re-triggered either.
        await h.PowerActions.Executions.AssertNoneAsync();

        // Task.Delay for the rearm is registered reactively (after the restart completes), so under heavy load a
        // single Advance() can race its registration - retry via the FakeTimeProvider overload.
        await h.Rearmed.NextAsync(h.TimeProvider, ShutdownCoordinator.ShutdownRearmDelay - ShutdownCoordinator.DefaultRearmDelay);
    }

    [Fact]
    public async Task Rearm_AfterSuccessfulSuspendAction_UsesSixtySecondDelay()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
            StopAction = StopAction.Suspend,
        });

        await PushReadingAsync(h, "OB", "10");
        Assert.Equal(StopAction.Suspend, await h.PowerActions.Executions.NextAsync());
        await WaitForRestartAsync(h);

        // Task.Delay for the rearm is registered reactively (after the restart completes), so under heavy load a
        // single Advance() can race its registration - retry via the FakeTimeProvider overload.
        await h.Rearmed.NextAsync(h.TimeProvider, ShutdownCoordinator.DefaultRearmDelay);
    }

    [Fact]
    public async Task Rearm_AfterFailedAction_RaisesShutdownFailed_RestartsTheMonitor_AndUsesSixtySecondDelay()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
            StopAction = StopAction.Shutdown,
        });
        h.PowerActions.ThrowOnExecute = new InvalidOperationException("power action unavailable");

        await PushReadingAsync(h, "OB", "10");

        var failure = await h.Failed.NextAsync();
        Assert.IsType<InvalidOperationException>(failure);
        await WaitForRestartAsync(h);
        Assert.Equal(MonitorState.Connected, h.Monitor.State);
        await h.PowerActions.Executions.AssertNoneAsync(); // the action never actually succeeded.

        // A failure re-arms after 60s, not the 5-minute Shutdown-success delay. Task.Delay for the rearm is
        // registered reactively (after the restart completes), so under heavy load a single Advance() can race
        // its registration - retry via the FakeTimeProvider overload.
        await h.Rearmed.NextAsync(h.TimeProvider, ShutdownCoordinator.DefaultRearmDelay);

        // A big single Advance() coalesces the monitor's periodic poll ticks (it does not fire once per elapsed
        // second), so one more explicit tick is needed for the now-rearmed policy to actually observe a poll.
        await h.PowerActions.Executions.NextAsync(h.TimeProvider, TimeSpan.FromSeconds(1)); // still-critical condition, now accepted again -> succeeds this time.
    }

    [Fact]
    public async Task Rearm_AfterDryRun_UsesSixtySecondDelay()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
            StopAction = StopAction.Shutdown,
        });
        h.Coordinator.DryRun = true;

        await PushReadingAsync(h, "OB", "10");
        await h.Executing.NextAsync();
        await WaitForRestartAsync(h);

        // Task.Delay for the rearm is registered reactively (after the restart completes), so under heavy load a
        // single Advance() can race its registration - retry via the FakeTimeProvider overload.
        await h.Rearmed.NextAsync(h.TimeProvider, ShutdownCoordinator.DefaultRearmDelay);
    }

    [Fact]
    public async Task NoRestartLoop_StillCriticalConditionAfterRestart_DoesNotRetriggerUntilRearmed()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = true,
            BatteryChargeFloor = 30,
            StopAction = StopAction.Suspend,
        });

        await PushReadingAsync(h, "OB", "10"); // triggers Start -> immediate execute -> restart.
        await h.PowerActions.Executions.NextAsync();
        await WaitForRestartAsync(h);

        // The UPS is still reporting the exact same critical condition (unchanged Variables) on every poll after
        // the restart; none of them may re-trigger until the policy re-arms.
        for (var i = 0; i < 5; i++)
        {
            h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
            await h.PowerActions.Executions.AssertNoneAsync(TimeSpan.FromMilliseconds(20));
        }

        Assert.Equal(MonitorState.Connected, h.Monitor.State); // monitoring kept running throughout.

        // Once re-armed, the same still-critical condition is accepted again. Task.Delay for the rearm is
        // registered reactively, so under heavy load a single Advance() can race its registration - retry via the
        // FakeTimeProvider overload. A further explicit tick is then needed since a big Advance() coalesces the
        // monitor's periodic poll ticks (it does not fire once per elapsed second).
        await h.Rearmed.NextAsync(h.TimeProvider, ShutdownCoordinator.DefaultRearmDelay);
        await h.PowerActions.Executions.NextAsync(h.TimeProvider, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        // Regression: exiting the app disposed the coordinator twice (explicitly and via the DI container), and the
        // second call threw ObjectDisposedException from the already disposed CancellationTokenSource.
        await using var h = await CreateStartedHarnessAsync();

        await h.Coordinator.DisposeAsync();
        await h.Coordinator.DisposeAsync();
    }
}
