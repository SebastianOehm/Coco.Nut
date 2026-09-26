using CocoNut.Core.Shutdown;
using CocoNut.Core.Tests.Monitoring;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.Core.Tests.Shutdown;

public class ShutdownCountdownTests
{
    [Fact]
    public void Start_ZeroDelay_CompletesImmediately()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        var completed = 0;
        countdown.Completed += (_, _) => completed++;

        countdown.Start(TimeSpan.Zero);

        Assert.Equal(1, completed);
        Assert.False(countdown.IsRunning);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
        Assert.Equal(1.0, countdown.Progress);
    }

    [Fact]
    public async Task Start_TicksEverySecond_ThenCompletes()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        var ticks = new EventCollector<TimeSpan>();
        var completed = new EventCollector<int>();
        countdown.Tick += (_, remaining) => ticks.Add(remaining);
        countdown.Completed += (_, _) => completed.Add(0);

        countdown.Start(TimeSpan.FromSeconds(3));
        Assert.True(countdown.IsRunning);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Total);
        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Remaining);

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(2), await ticks.NextAsync());

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(1), await ticks.NextAsync());

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await completed.NextAsync();

        Assert.False(countdown.IsRunning);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
    }

    [Fact]
    public async Task Progress_ReflectsElapsedFraction()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        var ticks = new EventCollector<TimeSpan>();
        countdown.Tick += (_, remaining) => ticks.Add(remaining);

        countdown.Start(TimeSpan.FromSeconds(10));
        Assert.Equal(0.0, countdown.Progress);

        timeProvider.Advance(TimeSpan.FromSeconds(5));
        await ticks.NextAsync();
        Assert.Equal(0.5, countdown.Progress, precision: 3);
    }

    [Fact]
    public void TryExtend_WhileRunning_ExtendsTotalAndRemainingOnce()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        countdown.Start(TimeSpan.FromSeconds(10));

        Assert.True(countdown.CanExtend);
        var extended = countdown.TryExtend(TimeSpan.FromSeconds(15));

        Assert.True(extended);
        Assert.Equal(TimeSpan.FromSeconds(25), countdown.Total);
        Assert.Equal(TimeSpan.FromSeconds(25), countdown.Remaining);
        Assert.False(countdown.CanExtend);
    }

    [Fact]
    public void TryExtend_CalledTwice_SecondCallFails()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        countdown.Start(TimeSpan.FromSeconds(10));

        Assert.True(countdown.TryExtend(TimeSpan.FromSeconds(15)));
        Assert.False(countdown.TryExtend(TimeSpan.FromSeconds(15)));
        Assert.Equal(TimeSpan.FromSeconds(25), countdown.Total);
    }

    [Fact]
    public void TryExtend_WhenNotRunning_Fails()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);

        Assert.False(countdown.TryExtend(TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void Cancel_WhileRunning_StopsAndRaisesCancelled()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        var cancelled = 0;
        countdown.Cancelled += (_, _) => cancelled++;
        var completed = 0;
        countdown.Completed += (_, _) => completed++;

        countdown.Start(TimeSpan.FromSeconds(10));
        countdown.Cancel();

        Assert.Equal(1, cancelled);
        Assert.False(countdown.IsRunning);

        // Advancing time after cancellation must not raise Completed.
        timeProvider.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(0, completed);
    }

    [Fact]
    public void Cancel_WhenNotRunning_DoesNothing()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        var cancelled = 0;
        countdown.Cancelled += (_, _) => cancelled++;

        countdown.Cancel();

        Assert.Equal(0, cancelled);
    }

    [Fact]
    public async Task Start_CalledAgain_RestartsWithNewDelay()
    {
        var timeProvider = new FakeTimeProvider();
        using var countdown = new ShutdownCountdown(timeProvider);
        countdown.Start(TimeSpan.FromSeconds(10));
        Assert.True(countdown.TryExtend(TimeSpan.FromSeconds(5)));

        countdown.Start(TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), countdown.Total);
        Assert.True(countdown.CanExtend); // a fresh Start resets the one-time extension.

        var completed = new EventCollector<int>();
        countdown.Completed += (_, _) => completed.Add(0);
        timeProvider.Advance(TimeSpan.FromSeconds(3));
        await completed.NextAsync();
    }
}
