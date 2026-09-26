using System.Threading;

namespace CocoNut.Core.Shutdown;

/// <summary>
/// A cancellable countdown with a single optional grace extension, driven by <see cref="TimeProvider"/> timers.
/// Ports WinNUT's <c>Shutdown_Gui.vb</c> (the <c>Shutdown_Timer</c>/<c>Grace_Timer</c> pair and the "Extend" button)
/// without any UI: the countdown window subscribes to <see cref="Tick"/>/<see cref="Completed"/>/<see cref="Cancelled"/>.
/// </summary>
public sealed class ShutdownCountdown : IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private ITimer? _timer;
    private DateTimeOffset _deadline;
    private bool _extended;
    private bool _disposed;

    public ShutdownCountdown(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>The delay passed to <see cref="Start"/>, plus one extension if <see cref="TryExtend"/> succeeded.</summary>
    public TimeSpan Total { get; private set; }

    /// <summary>Time left. Never negative; zero once <see cref="Completed"/> has been raised.</summary>
    public TimeSpan Remaining { get; private set; }

    /// <summary><see langword="true"/> between <see cref="Start"/> and completion/cancellation.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// <see langword="true"/> while the countdown is running and has not been extended yet. WinNUT's grace button
    /// (<c>PW_UserExtendStopTimer</c>) is disabled after one use; whether the button is offered at all is a policy
    /// decision for the caller (see <c>PowerSettings.AllowExtendDelay</c>), not this type.
    /// </summary>
    public bool CanExtend
    {
        get
        {
            lock (_gate)
            {
                return IsRunning && !_extended;
            }
        }
    }

    /// <summary>0 at start, 1 once <see cref="Remaining"/> reaches zero.</summary>
    public double Progress
    {
        get
        {
            var total = Total;
            if (total <= TimeSpan.Zero)
            {
                return 1.0;
            }

            return Math.Clamp(1.0 - Remaining.TotalSeconds / total.TotalSeconds, 0.0, 1.0);
        }
    }

    /// <summary>Raised about once per second while running, with the current <see cref="Remaining"/>.</summary>
    public event EventHandler<TimeSpan>? Tick;

    /// <summary>Raised once when <see cref="Remaining"/> reaches zero.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised once when <see cref="Cancel"/> stops a running countdown.</summary>
    public event EventHandler? Cancelled;

    /// <summary>
    /// Starts (or restarts) the countdown. A zero or negative delay completes synchronously, before this call
    /// returns, and raises <see cref="Completed"/> immediately.
    /// </summary>
    public void Start(TimeSpan delay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        StopTimer();

        bool completeNow;
        lock (_gate)
        {
            _extended = false;
            Total = delay;
            Remaining = delay;
            _deadline = _timeProvider.GetUtcNow() + delay;
            IsRunning = delay > TimeSpan.Zero;
            completeNow = !IsRunning;
        }

        if (completeNow)
        {
            Raise(Completed, EventArgs.Empty);
            return;
        }

        _timer = _timeProvider.CreateTimer(OnTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Extends the countdown by <paramref name="extra"/>, once (WinNUT's grace button). Returns
    /// <see langword="false"/> without effect when not running or already extended once.
    /// </summary>
    public bool TryExtend(TimeSpan extra)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (extra < TimeSpan.Zero)
        {
            extra = TimeSpan.Zero;
        }

        lock (_gate)
        {
            if (!IsRunning || _extended)
            {
                return false;
            }

            _extended = true;
            Total += extra;
            _deadline += extra;
            var remaining = _deadline - _timeProvider.GetUtcNow();
            Remaining = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            return true;
        }
    }

    /// <summary>Stops the countdown without executing anything. A no-op when not running.</summary>
    public void Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool wasRunning;
        lock (_gate)
        {
            wasRunning = IsRunning;
            IsRunning = false;
        }

        StopTimer();

        if (wasRunning)
        {
            Raise(Cancelled, EventArgs.Empty);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTimer();
    }

    private void OnTick(object? state)
    {
        bool completed;
        TimeSpan remaining;
        lock (_gate)
        {
            if (!IsRunning)
            {
                return;
            }

            remaining = _deadline - _timeProvider.GetUtcNow();
            completed = remaining <= TimeSpan.Zero;
            if (completed)
            {
                remaining = TimeSpan.Zero;
                IsRunning = false;
            }

            Remaining = remaining;
        }

        if (completed)
        {
            StopTimer();
            Raise(Completed, EventArgs.Empty);
        }
        else
        {
            Raise(Tick, remaining);
        }
    }

    private void StopTimer()
    {
        var timer = _timer;
        _timer = null;
        timer?.Dispose();
    }

    private void Raise(EventHandler? handler, EventArgs args)
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
            catch
            {
                // Swallowed: a misbehaving UI handler must not stop the countdown from completing (see UpsMonitor
                // for the equivalent, logged, guard - this type has no ILogger of its own by design).
            }
        }
    }

    private void Raise(EventHandler<TimeSpan>? handler, TimeSpan args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TimeSpan>)invocation)(this, args);
            }
            catch
            {
                // See the other Raise overload.
            }
        }
    }
}
