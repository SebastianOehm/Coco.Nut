using Avalonia.Headless.XUnit;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Shutdown;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.App.Tests;

public class ShutdownWindowTests
{
    [AvaloniaFact]
    public void Programmatic_close_succeeds_while_the_countdown_is_running()
    {
        // Regression: the window used to cancel every close while IsRunning was true, so the controller could not
        // close it after the shutdown was executed or cancelled and a stale countdown window stayed on screen.
        using var countdown = new ShutdownCountdown(new FakeTimeProvider());
        countdown.Start(TimeSpan.FromSeconds(60));
        using var viewModel = new ShutdownViewModel(
            ShutdownReason.BatteryChargeFloor,
            StopAction.Shutdown,
            countdown,
            allowExtend: false,
            dryRun: false,
            batteryChargeFloor: 30,
            runtimeFloorSeconds: 120,
            readingAtStart: null,
            currentReadingProvider: () => null,
            executeNow: () => Task.CompletedTask,
            tryExtend: () => false,
            new ImmediateUiDispatcher());
        Assert.True(viewModel.IsRunning);

        var window = new ShutdownWindow { DataContext = viewModel };
        var closed = false;
        window.Closed += (_, _) => closed = true;
        window.Show();

        window.Close();

        Assert.True(closed);
    }
}
