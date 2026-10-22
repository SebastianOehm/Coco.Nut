using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Tests.Fakes;
using CocoNut.Core.Tests.Monitoring;
using CocoNut.Core.Ups;
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

        /// <summary>When set, <see cref="ExecuteAsync"/> records the execution and then stays in progress until this
        /// source is completed, so a test can hold the coordinator's "already executing" guard.</summary>
        public TaskCompletionSource? Gate { get; set; }

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
            return Gate?.Task ?? Task.CompletedTask;
        }
    }

    private sealed class FakeRuleActionRunner : IRuleActionRunner
    {
        public EventCollector<(RuleMatch Match, UpsReading? Reading, string UpsName)> Runs { get; } = new();

        public Exception? Throw { get; set; }

        public void Run(RuleMatch match, UpsReading? reading, string upsName)
        {
            Runs.Add((match, reading, upsName));
            if (Throw is not null)
            {
                throw Throw;
            }
        }
    }

    private static ActionRule NotifyRule(string name, UpsStatus flags, bool alsoStop = false, StopAction stopAction = StopAction.Shutdown) => new()
    {
        Name = name,
        Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = flags },
        Actions = alsoStop ? [RuleAction.StopComputer(stopAction), RuleAction.Notify("hi")] : [RuleAction.Notify("hi")],
    };

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

    private static async Task<Harness> CreateStartedHarnessAsync(PowerSettings? settings = null, Func<IReadOnlyList<ActionRule>>? rules = null, IRuleActionRunner? runner = null)
    {
        var client = new FakeNutClient();
        client.Variables["ups.status"] = "OL";
        client.Variables["battery.charge"] = "80";

        var timeProvider = new FakeTimeProvider();
        var monitor = new UpsMonitor(() => client, timeProvider, NullLogger<UpsMonitor>.Instance);
        var powerActions = new FakePowerActions();
        var mutableSettings = new MutableSettings { Value = settings ?? new PowerSettings() };

        var coordinator = rules is null && runner is null
            ? new ShutdownCoordinator(
                monitor, powerActions, () => mutableSettings.Value, timeProvider, NullLogger<ShutdownCoordinator>.Instance)
            : new ShutdownCoordinator(
                monitor, powerActions, () => mutableSettings.Value,
                rules ?? (() => DefaultRules.FromPowerSettings(mutableSettings.Value)),
                timeProvider, NullLogger<ShutdownCoordinator>.Instance, runner);
        coordinator.DryRun = false;

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

        // The guard is reset when an execution ends, and the fake action completes immediately, so without a gate the
        // first execution could finish on the thread pool before the second call is made and the second one would
        // then legitimately run again. Holding the action open keeps the first execution provably in progress.
        h.PowerActions.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = h.Coordinator.ExecuteNowAsync();
        await h.PowerActions.Executions.NextAsync();

        await h.Coordinator.ExecuteNowAsync(); // returns without executing: the first execution is still running

        h.PowerActions.Gate.SetResult();
        await first;

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

    [Fact]
    public async Task ForcedShutdownReceivedOnLine_IsNotCancelledByTheNextReading()
    {
        // Deliberate change from the former fixed policy: the "power restored" cancel only applies to a stop that
        // was started while on battery. An FSD received while on line used to be cancelled by the very next reading.
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            RespectFsd = true,
        });

        await PushReadingAsync(h, "OL FSD");
        var pending = await h.Pending.NextAsync();
        Assert.Equal(ShutdownReason.ForcedShutdown, pending.Reason);

        await PushReadingAsync(h, "OL FSD");
        await PushReadingAsync(h, "OL");
        await h.Cancelled.AssertNoneAsync(TimeSpan.FromMilliseconds(50));
        Assert.True(h.Coordinator.IsShutdownPending);
    }

    [Fact]
    public async Task ForcedShutdownReceivedOnBattery_IsCancelledWhenPowerReturns()
    {
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            RespectFsd = true,
            BatteryChargeFloor = 0,
        });

        await PushReadingAsync(h, "OB FSD");
        Assert.Equal(ShutdownReason.ForcedShutdown, (await h.Pending.NextAsync()).Reason);

        await PushReadingAsync(h, "OL");
        Assert.Equal(ShutdownReason.PowerRestored, await h.Cancelled.NextAsync());
    }

    [Fact]
    public async Task CustomRule_ExecutesItsOwnStopAction_WithCustomRuleReason()
    {
        var rule = new ActionRule
        {
            Name = "Hibernate when load is high",
            Trigger = new RuleTrigger { Kind = TriggerKind.ValueAtOrAbove, Metric = RuleMetric.BatteryCharge, Threshold = 85 },
            Actions = [RuleAction.StopComputer(StopAction.Hibernate)],
        };
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = true, StopAction = StopAction.Shutdown },
            () => [rule]);

        await h.PowerActions.Executions.AssertNoneAsync(TimeSpan.FromMilliseconds(20));
        await PushReadingAsync(h, "OL", "90");

        Assert.Equal(StopAction.Hibernate, await h.PowerActions.Executions.NextAsync());
        Assert.Equal(ShutdownReason.CustomRule, await h.Executing.NextAsync());
    }

    [Fact]
    public async Task ConnectionLostRule_FiresAfterItsDurationWhileDisconnected()
    {
        var rule = new ActionRule
        {
            Name = "Stop when the server is gone",
            Trigger = new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 30 },
            Actions = [RuleAction.StopComputer(StopAction.Shutdown)],
        };
        await using var h = await CreateStartedHarnessAsync(new PowerSettings { StopImmediately = true }, () => [rule]);

        h.Client.ListVarsResults.Enqueue(new IOException("server gone")); // the next poll fails.
        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await h.StateChanges.NextAsync(); // Connected -> Disconnected (auto reconnect is off in the harness)

        h.TimeProvider.Advance(TimeSpan.FromSeconds(10));
        await h.PowerActions.Executions.AssertNoneAsync(TimeSpan.FromMilliseconds(50));

        // Time passes with no readings at all: only the coordinator's own tick can notice that 30s have elapsed.
        Assert.Equal(StopAction.Shutdown, await h.PowerActions.Executions.NextAsync(h.TimeProvider, TimeSpan.FromSeconds(10)));
    }

    [Theory]
    [InlineData("OL OB")]
    [InlineData("OB")]
    public async Task PendingStop_IsNotCancelledUnlessOnLineAndOffBattery(string status)
    {
        // Some UPS units briefly report OL and OB together during a transfer; only cancel once OB has cleared.
        await using var h = await CreateStartedHarnessAsync(new PowerSettings
        {
            StopImmediately = false,
            StopDelaySeconds = 30,
            BatteryChargeFloor = 30,
        });

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();

        await PushReadingAsync(h, status, "10");
        await h.Cancelled.AssertNoneAsync(TimeSpan.FromMilliseconds(50));
        Assert.True(h.Coordinator.IsShutdownPending);
    }

    [Fact]
    public async Task ConnectionLostRule_DoesNotFire_WhenTheUserDisconnectsAfterTheLoss()
    {
        var rule = new ActionRule
        {
            Name = "Stop when the server is gone",
            Trigger = new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 30 },
            Actions = [RuleAction.StopComputer(StopAction.Shutdown)],
        };
        await using var h = await CreateStartedHarnessAsync(new PowerSettings { StopImmediately = true }, () => [rule]);

        h.Client.ListVarsResults.Enqueue(new IOException("server gone"));
        h.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await h.StateChanges.NextAsync(); // Connected -> Disconnected with an error (lost)

        // A deliberate stop (graceful Disconnected, no error) ends the lost state.
        await h.Monitor.StopAsync();

        h.TimeProvider.Advance(TimeSpan.FromSeconds(60));
        h.TimeProvider.Advance(TimeSpan.FromSeconds(60));
        await h.PowerActions.Executions.AssertNoneAsync(TimeSpan.FromMilliseconds(100));
        Assert.False(h.Coordinator.IsShutdownPending);
    }

    // ---- non-stop actions ----

    [Fact]
    public async Task NonStopRule_RunsItsActionsOncePerRisingEdge_WithReadingAndUpsName()
    {
        var runner = new FakeRuleActionRunner();
        var rule = NotifyRule("On battery", UpsStatus.OB);
        await using var h = await CreateStartedHarnessAsync(rules: () => [rule], runner: runner);

        await PushReadingAsync(h, "OB", "55");
        var (match, reading, upsName) = await runner.Runs.NextAsync();
        Assert.Same(rule, match.Rule);
        Assert.True(match.IsNewFiring);
        Assert.Equal("OB", reading?.RawStatus);
        Assert.Equal(55, reading?.BatteryCharge);
        Assert.Equal("ups", upsName);

        await PushReadingAsync(h, "OB", "54"); // the condition still holds: no second run.
        await PushReadingAsync(h, "OB", "53");
        await runner.Runs.AssertNoneAsync(TimeSpan.FromMilliseconds(100));

        await PushReadingAsync(h, "OL", "53");
        await PushReadingAsync(h, "OB", "52"); // a new rising edge.
        Assert.Same(rule, (await runner.Runs.NextAsync()).Match.Rule);
    }

    [Fact]
    public async Task NonStopRule_StillRuns_WhileAStopIsPending()
    {
        var runner = new FakeRuleActionRunner();
        var rule = NotifyRule("Calibrating", UpsStatus.CAL);
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = false, StopDelaySeconds = 30, BatteryChargeFloor = 30 },
            () => [.. DefaultRules.FromPowerSettings(new PowerSettings { BatteryChargeFloor = 30 }), rule],
            runner);

        await PushReadingAsync(h, "OB", "10");
        await h.Pending.NextAsync();
        Assert.True(h.Coordinator.IsShutdownPending);

        await PushReadingAsync(h, "OB CAL", "10");

        Assert.Same(rule, (await runner.Runs.NextAsync()).Match.Rule);
    }

    [Fact]
    public async Task NonStopRule_StillRuns_WhileThePolicyIsRearming()
    {
        var runner = new FakeRuleActionRunner();
        var rule = NotifyRule("Calibrating", UpsStatus.CAL);
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = true, BatteryChargeFloor = 30 },
            () => [.. DefaultRules.FromPowerSettings(new PowerSettings { BatteryChargeFloor = 30 }), rule],
            runner);

        await PushReadingAsync(h, "OB", "10");
        await h.PowerActions.Executions.NextAsync();
        await WaitForRestartAsync(h); // the policy is disarmed now.

        await PushReadingAsync(h, "OB CAL", "10");

        Assert.Same(rule, (await runner.Runs.NextAsync()).Match.Rule);
    }

    [Fact]
    public async Task StopRuleWithAnExtraNotify_NotifiesOnce_NotOnEveryPoll()
    {
        var runner = new FakeRuleActionRunner();
        var rule = NotifyRule("Stop and tell", UpsStatus.OB, alsoStop: true);
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = false, StopDelaySeconds = 60 }, () => [rule], runner);

        await PushReadingAsync(h, "OB", "50");
        await h.Pending.NextAsync();
        Assert.True((await runner.Runs.NextAsync()).Match.IsNewFiring);

        await PushReadingAsync(h, "OB", "49");
        await PushReadingAsync(h, "OB", "48");
        await runner.Runs.AssertNoneAsync(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task ARunnerThatThrows_NeverStopsTheStopLogic()
    {
        var runner = new FakeRuleActionRunner { Throw = new InvalidOperationException("boom") };
        var rule = NotifyRule("Stop and tell", UpsStatus.OB, alsoStop: true);
        await using var h = await CreateStartedHarnessAsync(new PowerSettings { StopImmediately = true }, () => [rule], runner);

        await PushReadingAsync(h, "OB", "50");

        Assert.Equal(StopAction.Shutdown, await h.PowerActions.Executions.NextAsync());
    }

    [Fact]
    public async Task BuiltInStopRules_StillStopAsBefore_WithARunnerAttached()
    {
        var runner = new FakeRuleActionRunner();
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = true, BatteryChargeFloor = 30 }, runner: runner);

        await PushReadingAsync(h, "OB", "10");

        Assert.Equal(StopAction.Shutdown, await h.PowerActions.Executions.NextAsync());
        Assert.Equal(ShutdownReason.BatteryChargeFloor, await h.Executing.NextAsync());
    }

    // ---- custom rule stop / RuleFired ----

    [Fact]
    public async Task CustomRuleStop_CarriesTheRuleNameInPendingAndWhileExecuting()
    {
        var rule = NotifyRule("Stop at night", UpsStatus.OB, alsoStop: true);
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = false, StopDelaySeconds = 30 }, () => [rule]);

        await PushReadingAsync(h, "OB", "50");

        var pending = await h.Pending.NextAsync();
        Assert.Equal(ShutdownReason.CustomRule, pending.Reason);
        Assert.Equal("Stop at night", pending.RuleName);
        Assert.Equal("Stop at night", h.Coordinator.PendingRuleName);
    }

    [Fact]
    public async Task RuleFired_IsRaisedOncePerNewFiringOfAUserRule_AndNotForBuiltInRules()
    {
        var userRule = NotifyRule("Mine", UpsStatus.CAL);
        var fired = new EventCollector<RuleFiredEventArgs>();
        await using var h = await CreateStartedHarnessAsync(
            new PowerSettings { StopImmediately = false, StopDelaySeconds = 60, BatteryChargeFloor = 30 },
            () => [.. DefaultRules.FromPowerSettings(new PowerSettings { BatteryChargeFloor = 30 }), userRule]);
        h.Coordinator.RuleFired += (_, e) => fired.Add(e);

        await PushReadingAsync(h, "OB", "10"); // the built-in charge floor fires, the user rule does not.
        await h.Pending.NextAsync();
        await fired.AssertNoneAsync(TimeSpan.FromMilliseconds(100));

        await PushReadingAsync(h, "OB CAL", "10");
        var e = await fired.NextAsync();
        Assert.Same(userRule, e.Rule);

        await PushReadingAsync(h, "OB CAL", "10");
        await fired.AssertNoneAsync(TimeSpan.FromMilliseconds(100));
    }
}
