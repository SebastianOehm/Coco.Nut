using System.Threading;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Actions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Shutdown;

/// <summary>
/// Wires <see cref="UpsMonitor"/> readings through the <see cref="RuleEngine"/> and its <see cref="ActionRule"/>s to
/// a countdown (or an immediate stop) and finally to <see cref="IPowerActions"/>. Ports WinNUT's
/// <c>Shutdown_Event</c>/<c>Stop_Shutdown_Event</c>/<c>Shutdown_Action</c> in <c>WinNUT.vb</c>.
/// <para>
/// Stop rules are level triggered: while their condition holds the engine reports them on every evaluation, and
/// this class ignores the repeats while a stop is pending (<c>_shutdownPending</c>) or the policy is still
/// re-arming (<c>_armed</c>); once re-armed, a condition that still holds starts the stop again.
/// </para>
/// <para>
/// Every other action of a rule (notify, log, command, webhook) is handed to the <see cref="IRuleActionRunner"/> once
/// per <see cref="RuleMatch.IsNewFiring"/>, before and independent of the stop logic: also while a stop is pending
/// and while the policy is re-arming.
/// </para>
/// <para>
/// A pending stop is cancelled when mains power returns (<c>OL</c> present, <c>OB</c> cleared), but only if the
/// stop was started while the UPS was on battery. A stop started on line power (for example a forced shutdown
/// received while on line) is not cancelled by the next reading, which the previous fixed policy did.
/// </para>
/// </summary>
public sealed class ShutdownCoordinator : IAsyncDisposable
{
    /// <summary>
    /// How long the stop policy stays disarmed after a successful <see cref="StopAction.Shutdown"/>: the machine is
    /// on its way down (writing to disk, powering off), so there is no rush and no risk of re-triggering while it
    /// is still up.
    /// </summary>
    internal static readonly TimeSpan ShutdownRearmDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long the stop policy stays disarmed after anything else that still leaves the machine running: a
    /// successful <see cref="StopAction.Suspend"/>/<see cref="StopAction.Hibernate"/> (which wakes back up), a
    /// failed action, or <see cref="DryRun"/>. Short, because the machine needs protecting again soon, but long
    /// enough to avoid immediately re-triggering on the very next poll while the same condition (e.g. still on
    /// battery, still low) is observed right after the monitor restarts.
    /// </summary>
    internal static readonly TimeSpan DefaultRearmDelay = TimeSpan.FromSeconds(60);

    private readonly UpsMonitor _monitor;
    private readonly IPowerActions _powerActions;
    private readonly Func<PowerSettings> _settingsProvider;
    private readonly Func<IReadOnlyList<ActionRule>> _rulesProvider;
    private readonly RuleEngine _engine;
    private readonly IRuleActionRunner? _actionRunner;
    private readonly Lock _engineGate = new();
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    private ShutdownCountdown? _countdown;
    private bool _shutdownPending;
    private bool _executed;
    private bool _armed = true;
    private ShutdownReason _pendingReason;
    private StopAction? _pendingStopAction;
    private string? _pendingRuleName;
    private bool _startedOnBattery;
    private bool _connectionLost;
    private ITimer? _connectionTimer;

    /// <summary>Creates a coordinator running the built-in <see cref="DefaultRules"/> derived from the power settings.</summary>
    public ShutdownCoordinator(
        UpsMonitor monitor,
        IPowerActions powerActions,
        Func<PowerSettings> settingsProvider,
        TimeProvider timeProvider,
        ILogger<ShutdownCoordinator> logger,
        IRuleActionRunner? actionRunner = null)
        : this(monitor, powerActions, settingsProvider, () => DefaultRules.FromPowerSettings(settingsProvider()), timeProvider, logger, actionRunner)
    {
    }

    /// <param name="monitor">The UPS monitor to watch.</param>
    /// <param name="powerActions">Executes the power action.</param>
    /// <param name="settingsProvider">Countdown, extend and immediate-stop settings (and the stop action fallback).</param>
    /// <param name="rulesProvider">
    /// The rule set, asked on every evaluation so changes apply at once. It may return new instances each time: the
    /// engine's per-rule state is keyed by <see cref="ActionRule.Id"/>.
    /// </param>
    /// <param name="timeProvider">Time source for the engine, countdown and re-arm delay.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="actionRunner">Runs the actions other than stopping the computer; <see langword="null"/> = they are ignored.</param>
    public ShutdownCoordinator(
        UpsMonitor monitor,
        IPowerActions powerActions,
        Func<PowerSettings> settingsProvider,
        Func<IReadOnlyList<ActionRule>> rulesProvider,
        TimeProvider timeProvider,
        ILogger<ShutdownCoordinator> logger,
        IRuleActionRunner? actionRunner = null)
    {
        _actionRunner = actionRunner;
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _powerActions = powerActions ?? throw new ArgumentNullException(nameof(powerActions));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _rulesProvider = rulesProvider ?? throw new ArgumentNullException(nameof(rulesProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _engine = new RuleEngine(timeProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // WinNUT stubbed the actual power action out under an attached debugger (Shutdown_Action, #If DEBUG).
        // COCONUT_DRY_RUN=1 is the equivalent developer safety net for a released build.
        DryRun = Environment.GetEnvironmentVariable("COCONUT_DRY_RUN") == "1";

        _monitor.ReadingUpdated += OnReadingUpdated;
        _monitor.StatusChanged += OnStatusChanged;
        _monitor.ConnectionLost += OnConnectionLost;
        _monitor.StateChanged += OnStateChanged;
    }

    /// <summary>
    /// When <see langword="true"/>, the moment the power action would run is logged instead of executing it.
    /// Defaults to <see langword="true"/> when the <c>COCONUT_DRY_RUN</c> environment variable is <c>"1"</c>.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary><see langword="true"/> from the moment a shutdown starts until it is cancelled or executed.</summary>
    public bool IsShutdownPending
    {
        get
        {
            lock (_gate)
            {
                return _shutdownPending;
            }
        }
    }

    /// <summary>The name of the rule whose stop is pending, or <see langword="null"/> when nothing is pending.</summary>
    public string? PendingRuleName
    {
        get
        {
            lock (_gate)
            {
                return _shutdownPending ? _pendingRuleName : null;
            }
        }
    }

    /// <summary>The active countdown, or <see langword="null"/> when nothing is pending or the stop is immediate.</summary>
    public ShutdownCountdown? Countdown
    {
        get
        {
            lock (_gate)
            {
                return _countdown;
            }
        }
    }

    /// <summary>
    /// Raised when the stop procedure starts and is not immediate, so the UI can show a countdown window
    /// (WinNUT: opening <c>Shutdown_Gui</c>).
    /// </summary>
    public event EventHandler<ShutdownPendingEventArgs>? ShutdownPending;

    /// <summary>
    /// Raised for every new firing (see <see cref="RuleMatch.IsNewFiring"/>) of a user-defined rule, not for the
    /// built-in stop rules (their stops are reported by <see cref="ShutdownPending"/> and friends).
    /// </summary>
    public event EventHandler<RuleFiredEventArgs>? RuleFired;

    /// <summary>Raised when a pending shutdown is cancelled because mains power returned.</summary>
    public event EventHandler<ShutdownReason>? ShutdownCancelled;

    /// <summary>Raised right before the UPS monitor is stopped and the power action is invoked (or logged, in dry-run).</summary>
    public event EventHandler<ShutdownReason>? ShutdownExecuting;

    /// <summary>Raised when <see cref="IPowerActions.ExecuteAsync"/> throws.</summary>
    public event EventHandler<Exception>? ShutdownFailed;

    /// <summary>
    /// Raised when the stop policy becomes willing to react to a new stop condition again, once the re-arm delay
    /// after the previous action has elapsed (see <see cref="ShutdownRearmDelay"/>/<see cref="DefaultRearmDelay"/>).
    /// Purely informational; the coordinator enforces the cooldown itself either way.
    /// </summary>
    public event EventHandler? Rearmed;

    private void OnReadingUpdated(object? sender, UpsReading reading) => Evaluate(reading, null);

    private void OnStatusChanged(object? sender, UpsStatusChangedEventArgs e) => Evaluate(_monitor.LastReading, e);

    private void OnConnectionLost(object? sender, Exception? error)
    {
        lock (_gate)
        {
            _connectionLost = true;
            // Duration triggers need evaluations while no readings arrive; tick once a second until reconnected.
            _connectionTimer ??= _timeProvider.CreateTimer(_ => Evaluate(null, null), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        Evaluate(null, null);
    }

    private void OnStateChanged(object? sender, MonitorStateChangedEventArgs e)
    {
        // The lost state ends in two cases: the connection is back (Connected), or the monitor was stopped on
        // purpose (Disconnected without an error - the user disconnected, or this coordinator's own LOGOUT before a
        // power action). A Disconnected *with* an error (lost, auto-reconnect off) is still a lost connection and
        // keeps counting towards connection-lost rules.
        var ended = e.NewState == MonitorState.Connected
            || (e.NewState == MonitorState.Disconnected && e.Error is null);
        if (!ended)
        {
            return;
        }

        ITimer? timer;
        lock (_gate)
        {
            _connectionLost = false;
            timer = _connectionTimer;
            _connectionTimer = null;
        }

        timer?.Dispose();
    }

    private void Evaluate(UpsReading? reading, UpsStatusChangedEventArgs? statusChange)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        bool connectionLost;
        lock (_gate)
        {
            connectionLost = _connectionLost;
        }

        IReadOnlyList<RuleMatch> matches;
        lock (_engineGate)
        {
            matches = _engine.Evaluate(reading, statusChange, connectionLost, _rulesProvider());
        }

        DispatchActions(matches, reading);

        if (IsShutdownPending)
        {
            // Only the "power restored" cancel rule applies while pending - a stop is never started a second time.
            if (reading is not null && ShouldCancel(reading))
            {
                CancelPending(ShutdownReason.PowerRestored, "Mains power (OL) is present again and on-battery (OB) has cleared.");
            }

            return;
        }

        foreach (var match in matches)
        {
            var stop = match.Rule.Actions.FirstOrDefault(a => a.Kind == ActionKind.StopComputer);
            if (stop is not null)
            {
                Start(match, stop.StopAction, reading, _settingsProvider());
                return; // the first firing stop rule (list order = priority) decides.
            }
        }
    }

    /// <summary>Reports the new firings and hands their non-stop actions to the runner. Never throws.</summary>
    private void DispatchActions(IReadOnlyList<RuleMatch> matches, UpsReading? reading)
    {
        var upsName = _monitor.Settings?.UpsName ?? string.Empty;
        foreach (var match in matches)
        {
            if (!match.IsNewFiring)
            {
                continue;
            }

            if (!DefaultRules.IsBuiltIn(match.Rule.Id))
            {
                _logger.LogInformation("Rule '{Rule}' fired: {Detail}", match.Rule.Name, match.Detail);
                RaiseSafe(RuleFired, new RuleFiredEventArgs(match.Rule, match.Detail));
            }

            if (_actionRunner is null || !match.Rule.Actions.Any(a => a.Kind != ActionKind.StopComputer))
            {
                continue;
            }

            try
            {
                _actionRunner.Run(match, reading, upsName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start the actions of rule '{Rule}'.", match.Rule.Name);
            }
        }
    }

    private bool ShouldCancel(UpsReading reading)
    {
        if (!reading.Status.HasFlag(UpsStatus.OL) || reading.Status.HasFlag(UpsStatus.OB))
        {
            return false;
        }

        lock (_gate)
        {
            return _startedOnBattery;
        }
    }

    private void Start(RuleMatch match, StopAction stopAction, UpsReading? reading, PowerSettings settings)
    {
        var reason = match.Rule.ShutdownReason;
        lock (_gate)
        {
            if (_shutdownPending)
            {
                return;
            }

            if (!_armed)
            {
                // Still cooling down after the previous action restarted the monitor - without this, the same
                // still-critical condition observed on the very next poll would start (and execute) a shutdown
                // again immediately, in a tight loop.
                _logger.LogDebug(
                    "Ignoring a new stop condition ({Reason}) - the stop policy is still re-arming.", reason);
                return;
            }

            _shutdownPending = true;
            _pendingReason = reason;
            _pendingStopAction = stopAction;
            _pendingRuleName = match.Rule.Name;
            _startedOnBattery = reading?.Status.HasFlag(UpsStatus.OB) ?? false;
        }

        _logger.LogWarning("Shutdown starting: {Reason} - rule '{Rule}': {Detail}", reason, match.Rule.Name, match.Detail);

        if (settings.StopImmediately)
        {
            _ = ExecuteAsync(reason, stopAction);
            return;
        }

        var countdown = new ShutdownCountdown(_timeProvider);
        lock (_gate)
        {
            _countdown = countdown;
        }

        countdown.Completed += (_, _) => _ = ExecuteAsync(reason, stopAction);
        RaiseSafe(ShutdownPending, new ShutdownPendingEventArgs(reason, countdown, match.Rule.Name));
        countdown.Start(TimeSpan.FromSeconds(settings.StopDelaySeconds));
    }

    private void CancelPending(ShutdownReason reason, string detail)
    {
        ShutdownCountdown? countdown;
        lock (_gate)
        {
            if (_executed)
            {
                // Execution has already started (LOGOUT in progress or done, the action may already be running) -
                // it is too late to cancel; let ExecuteAsync run to completion and re-arm afterwards.
                return;
            }

            if (!_shutdownPending)
            {
                return;
            }

            _shutdownPending = false;
            countdown = _countdown;
            _countdown = null;
        }

        countdown?.Cancel();
        countdown?.Dispose();
        _logger.LogInformation("Shutdown cancelled: {Detail}", detail);
        RaiseSafe(ShutdownCancelled, reason);
    }

    /// <summary>
    /// Extends the running countdown by <c>PowerSettings.ExtendDelaySeconds</c> (WinNUT's grace button). Returns
    /// <see langword="false"/> when nothing is pending, the setting disallows it, or the one extension was already used.
    /// </summary>
    public bool TryExtend()
    {
        var settings = _settingsProvider();
        if (!settings.AllowExtendDelay)
        {
            return false;
        }

        ShutdownCountdown? countdown;
        lock (_gate)
        {
            countdown = _countdown;
        }

        return countdown is not null && countdown.TryExtend(TimeSpan.FromSeconds(settings.ExtendDelaySeconds));
    }

    /// <summary>
    /// Skips the rest of the countdown and executes immediately (WinNUT: <c>Shutdown_Gui.ShutDown_Btn_Click</c>).
    /// A no-op beyond stopping the countdown's own ticking if the action already ran.
    /// </summary>
    public Task ExecuteNowAsync()
    {
        ShutdownReason reason;
        StopAction? stopAction;
        ShutdownCountdown? countdown;
        lock (_gate)
        {
            reason = _shutdownPending ? _pendingReason : ShutdownReason.ForcedShutdown;
            stopAction = _shutdownPending ? _pendingStopAction : null;
            countdown = _countdown;
        }

        // Stop the countdown's own timer so its Completed handler cannot also call ExecuteAsync.
        countdown?.Cancel();
        return ExecuteAsync(reason, stopAction ?? _settingsProvider().StopAction);
    }

    /// <summary>
    /// Stops the monitor (LOGOUT - the NUT primary waits for secondaries to log out before cutting power),
    /// executes (or, in <see cref="DryRun"/>, logs) the power action, then restarts the monitor and resets the
    /// coordinator's state, regardless of whether the action succeeded, failed, or was skipped: a
    /// <see cref="StopAction.Suspend"/>/<see cref="StopAction.Hibernate"/> wakes the machine back up, a failed
    /// action and <see cref="DryRun"/> never took the machine down at all, and even a genuine
    /// <see cref="StopAction.Shutdown"/> is not guaranteed to actually happen (the OS can refuse it) - in every
    /// case, Coco.Nut must keep protecting the machine rather than staying disconnected forever. A new stop
    /// decision is only accepted again once the re-arm delay has elapsed (see <see cref="ShutdownRearmDelay"/>/
    /// <see cref="DefaultRearmDelay"/>), so the same still-critical condition observed right after the restart does
    /// not immediately re-trigger a loop of shutdown attempts.
    /// </summary>
    private async Task ExecuteAsync(ShutdownReason reason, StopAction stopAction)
    {
        lock (_gate)
        {
            if (_executed)
            {
                return;
            }

            _executed = true;
        }

        RaiseSafe(ShutdownExecuting, reason);

        try
        {
            await _monitor.StopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping the UPS monitor before shutting down.");
        }

        var succeeded = false;
        if (DryRun)
        {
            _logger.LogWarning("DRY RUN ({Reason}): would execute power action {Action} now.", reason, stopAction);
        }
        else
        {
            try
            {
                await _powerActions.ExecuteAsync(stopAction).ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute power action {Action}.", stopAction);
                RaiseSafe(ShutdownFailed, ex);
            }
        }

        try
        {
            await _monitor.RestartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to restart the UPS monitor after a shutdown action.");
        }

        ShutdownCountdown? countdown;
        lock (_gate)
        {
            _shutdownPending = false;
            countdown = _countdown;
            _countdown = null;
            _executed = false;
            _armed = false;
            _pendingStopAction = null;
            _pendingRuleName = null;
            _startedOnBattery = false;
        }

        countdown?.Dispose();

        var rearmDelay = succeeded && stopAction == StopAction.Shutdown ? ShutdownRearmDelay : DefaultRearmDelay;
        _ = RearmAfterDelayAsync(rearmDelay);
    }

    private async Task RearmAfterDelayAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _timeProvider, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Disposed before the cooldown elapsed.
        }

        lock (_gate)
        {
            _armed = true;
        }

        _logger.LogInformation("The stop policy is re-armed.");
        RaiseSafe(Rearmed, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        // Idempotent: the app disposes the coordinator explicitly (before the monitor) and the DI container
        // disposes it again on shutdown; a second Cancel() on the disposed token source used to throw.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _monitor.ReadingUpdated -= OnReadingUpdated;
        _monitor.StatusChanged -= OnStatusChanged;
        _monitor.ConnectionLost -= OnConnectionLost;
        _monitor.StateChanged -= OnStateChanged;

        _disposeCts.Cancel();
        _disposeCts.Dispose();

        lock (_gate)
        {
            _countdown?.Dispose();
            _countdown = null;
            _connectionTimer?.Dispose();
            _connectionTimer = null;
        }

        return ValueTask.CompletedTask;
    }

    private void RaiseSafe<TArgs>(EventHandler<TArgs>? handler, TArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)invocation)(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in a ShutdownCoordinator event handler.");
            }
        }
    }

    private void RaiseSafe(EventHandler? handler, EventArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)invocation)(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in a ShutdownCoordinator event handler.");
            }
        }
    }
}
