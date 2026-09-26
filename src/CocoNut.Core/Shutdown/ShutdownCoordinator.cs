using System.Threading;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Shutdown;

/// <summary>
/// Wires <see cref="UpsMonitor"/> readings through <see cref="ShutdownPolicy"/> to a countdown (or an immediate
/// stop) and finally to <see cref="IPowerActions"/>. Ports WinNUT's <c>Shutdown_Event</c>/<c>Stop_Shutdown_Event</c>/
/// <c>Shutdown_Action</c> in <c>WinNUT.vb</c>.
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
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private ShutdownCountdown? _countdown;
    private bool _shutdownPending;
    private bool _executed;
    private bool _armed = true;
    private ShutdownReason _pendingReason;

    public ShutdownCoordinator(
        UpsMonitor monitor,
        IPowerActions powerActions,
        Func<PowerSettings> settingsProvider,
        TimeProvider timeProvider,
        ILogger<ShutdownCoordinator> logger)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _powerActions = powerActions ?? throw new ArgumentNullException(nameof(powerActions));
        _settingsProvider = settingsProvider ?? throw new ArgumentNullException(nameof(settingsProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // WinNUT stubbed the actual power action out under an attached debugger (Shutdown_Action, #If DEBUG).
        // COCONUT_DRY_RUN=1 is the equivalent developer safety net for a released build.
        DryRun = Environment.GetEnvironmentVariable("COCONUT_DRY_RUN") == "1";

        _monitor.ReadingUpdated += OnReadingUpdated;
        _monitor.StatusChanged += OnStatusChanged;
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

    private void Evaluate(UpsReading? reading, UpsStatusChangedEventArgs? statusChange)
    {
        if (reading is null)
        {
            return;
        }

        var settings = _settingsProvider();
        var decision = ShutdownPolicy.Evaluate(reading, statusChange, settings, IsShutdownPending);

        switch (decision.Kind)
        {
            case ShutdownDecisionKind.Start:
                Start(decision, settings);
                break;
            case ShutdownDecisionKind.Cancel:
                CancelPending(decision);
                break;
        }
    }

    private void Start(ShutdownDecision decision, PowerSettings settings)
    {
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
                    "Ignoring a new stop condition ({Reason}) - the stop policy is still re-arming.", decision.Reason);
                return;
            }

            _shutdownPending = true;
            _pendingReason = decision.Reason;
        }

        _logger.LogWarning("Shutdown starting: {Reason} - {Detail}", decision.Reason, decision.Detail);

        if (settings.StopImmediately)
        {
            _ = ExecuteAsync(decision.Reason);
            return;
        }

        var countdown = new ShutdownCountdown(_timeProvider);
        lock (_gate)
        {
            _countdown = countdown;
        }

        countdown.Completed += (_, _) => _ = ExecuteAsync(decision.Reason);
        RaiseSafe(ShutdownPending, new ShutdownPendingEventArgs(decision.Reason, countdown));
        countdown.Start(TimeSpan.FromSeconds(settings.StopDelaySeconds));
    }

    private void CancelPending(ShutdownDecision decision)
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
        _logger.LogInformation("Shutdown cancelled: {Detail}", decision.Detail);
        RaiseSafe(ShutdownCancelled, decision.Reason);
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
        ShutdownCountdown? countdown;
        lock (_gate)
        {
            reason = _shutdownPending ? _pendingReason : ShutdownReason.ForcedShutdown;
            countdown = _countdown;
        }

        // Stop the countdown's own timer so its Completed handler cannot also call ExecuteAsync.
        countdown?.Cancel();
        return ExecuteAsync(reason);
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
    private async Task ExecuteAsync(ShutdownReason reason)
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
        var settings = _settingsProvider();

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
            _logger.LogWarning("DRY RUN ({Reason}): would execute power action {Action} now.", reason, settings.StopAction);
        }
        else
        {
            try
            {
                await _powerActions.ExecuteAsync(settings.StopAction).ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute power action {Action}.", settings.StopAction);
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
        }

        countdown?.Dispose();

        var rearmDelay = succeeded && settings.StopAction == StopAction.Shutdown ? ShutdownRearmDelay : DefaultRearmDelay;
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
        _monitor.ReadingUpdated -= OnReadingUpdated;
        _monitor.StatusChanged -= OnStatusChanged;

        _disposeCts.Cancel();
        _disposeCts.Dispose();

        lock (_gate)
        {
            _countdown?.Dispose();
            _countdown = null;
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
