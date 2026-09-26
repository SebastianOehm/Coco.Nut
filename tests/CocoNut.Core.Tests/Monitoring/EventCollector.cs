using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Monitoring;

/// <summary>
/// Collects events raised on a background thread (or synchronously re-entrantly) into an ordered queue an async
/// test method can await, instead of racing a plain field/list against the code under test. Every monitor/shutdown
/// test in this project waits for events this way rather than sleeping.
/// </summary>
internal sealed class EventCollector<T>
{
    private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();

    public void Add(T item) => _channel.Writer.TryWrite(item);

    /// <summary>Waits for the next collected item, in the order it was added. Throws <see cref="TimeoutException"/>
    /// if none arrives within <paramref name="timeout"/> (default 5 seconds, per the test time budget - generous
    /// on purpose: under heavy CPU load, the code under test's own async continuations can simply take a while to
    /// be scheduled even though nothing is logically wrong, and the fast path pays nothing extra).</summary>
    /// <remarks>
    /// Uses a <see cref="CancellationToken"/> (rather than racing <see cref="ChannelReader{T}.ReadAsync()"/> against
    /// <see cref="Task.Delay(TimeSpan)"/> with <see cref="Task.WhenAny(Task[])"/>) so a timed-out wait properly
    /// deregisters itself as a waiting consumer instead of silently stealing a later, legitimately-awaited item.
    /// </remarks>
    public async Task<T> NextAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for a {typeof(T).Name} event.");
        }
    }

    /// <summary>
    /// Robust version of <see cref="NextAsync(TimeSpan?)"/> for an item that depends on a <em>fresh, one-shot</em>
    /// <c>Task.Delay</c> (or similar single-use timer) the code under test registers on <paramref name="timeProvider"/>
    /// reactively - e.g. after a multi-step async chain triggered by an earlier event (a reconnect's backoff delay,
    /// a re-arm cooldown) - as opposed to one already sitting parked at a point some earlier, already-confirmed
    /// event happens-after in program order (a steady <see cref="PeriodicTimer"/> tick, or an
    /// <see cref="System.Threading.ITimer"/> the test created synchronously itself, need no such care: their
    /// registration is already guaranteed to exist).
    /// </summary>
    /// <remarks>
    /// A single <see cref="FakeTimeProvider.Advance(TimeSpan)"/> call can be "lost" for a fresh one-shot delay:
    /// if the code that calls <c>Task.Delay</c> has not run yet (its continuation is still queued on a busy thread
    /// pool) when <c>Advance</c> runs, there is nothing registered yet to advance past. Once that call does happen,
    /// its due time is computed from whatever <paramref name="timeProvider"/> already reads by then - later than
    /// intended - so the earlier advance never reaches it. Retrying fixes this: each retry moves the clock forward
    /// by another <paramref name="step"/>, so once the registration exists, the very next retry's advance is
    /// guaranteed to reach (or pass) its due time, however late the registration itself was. Bounded by an overall
    /// real-time <paramref name="timeout"/> (default 5s, the test time budget); the common/fast case (the
    /// registration already exists) costs nothing extra - the first attempt succeeds immediately.
    /// </remarks>
    public async Task<T> NextAsync(FakeTimeProvider timeProvider, TimeSpan step, TimeSpan? timeout = null)
    {
        var overall = timeout ?? TimeSpan.FromSeconds(5);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            timeProvider.Advance(step);

            try
            {
                return await NextAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                if (stopwatch.Elapsed >= overall)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>Asserts that nothing arrives within <paramref name="quietPeriod"/> (default 150ms).</summary>
    public async Task AssertNoneAsync(TimeSpan? quietPeriod = null)
    {
        using var cts = new CancellationTokenSource(quietPeriod ?? TimeSpan.FromMilliseconds(150));
        try
        {
            var item = await _channel.Reader.ReadAsync(cts.Token);
            throw new InvalidOperationException($"Expected no {typeof(T).Name} event, but one arrived: {item}.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Nothing arrived within the quiet period, as expected.
        }
    }
}
