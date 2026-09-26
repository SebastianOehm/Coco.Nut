using System.Threading;
using System.Threading.Channels;

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
    /// if none arrives within <paramref name="timeout"/> (default 2 seconds, per the test time budget).</summary>
    /// <remarks>
    /// Uses a <see cref="CancellationToken"/> (rather than racing <see cref="ChannelReader{T}.ReadAsync()"/> against
    /// <see cref="Task.Delay(TimeSpan)"/> with <see cref="Task.WhenAny(Task[])"/>) so a timed-out wait properly
    /// deregisters itself as a waiting consumer instead of silently stealing a later, legitimately-awaited item.
    /// </remarks>
    public async Task<T> NextAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
        try
        {
            return await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for a {typeof(T).Name} event.");
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
