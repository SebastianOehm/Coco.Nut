using System.Globalization;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using CocoNut.Localization;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.App.Tests.ViewModels;

public class ShutdownViewModelTests
{
    public ShutdownViewModelTests() => Strings.Culture = CultureInfo.GetCultureInfo("en");

    private static (ShutdownViewModel ViewModel, ShutdownCountdown Countdown) Build(
        ShutdownReason reason = ShutdownReason.BatteryChargeFloor,
        StopAction stopAction = StopAction.Shutdown,
        bool allowExtend = true,
        bool dryRun = false,
        TimeSpan? delay = null,
        FakeTimeProvider? timeProvider = null,
        Func<Task>? executeNow = null,
        UpsReading? readingAtStart = null)
    {
        var time = timeProvider ?? new FakeTimeProvider();
        var countdown = new ShutdownCountdown(time);
        countdown.Start(delay ?? TimeSpan.FromSeconds(90));

        var vm = new ShutdownViewModel(
            reason,
            stopAction,
            countdown,
            allowExtend,
            dryRun,
            batteryChargeFloor: 30,
            runtimeFloorSeconds: 120,
            readingAtStart,
            currentReadingProvider: () => readingAtStart,
            executeNow: executeNow ?? (() => Task.CompletedTask),
            tryExtend: () => countdown.TryExtend(TimeSpan.FromSeconds(15)),
            new ImmediateUiDispatcher());

        return (vm, countdown);
    }

    [Fact]
    public void Countdown_under_one_hour_is_formatted_as_mmss()
    {
        var (vm, countdown) = Build(delay: TimeSpan.FromSeconds(90));

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_Countdown, Strings.StopAction_Shutdown, "01:30"), vm.CountdownText);

        countdown.Dispose();
    }

    [Fact]
    public void Countdown_at_or_above_one_hour_is_formatted_as_hmmss()
    {
        var (vm, countdown) = Build(delay: TimeSpan.FromSeconds(3700));

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_Countdown, Strings.StopAction_Shutdown, "1:01:40"), vm.CountdownText);

        countdown.Dispose();
    }

    [Fact]
    public void Grace_button_is_hidden_when_extension_is_not_allowed()
    {
        var (vm, countdown) = Build(allowExtend: false);

        Assert.False(vm.CanShowGraceButton);

        countdown.Dispose();
    }

    [Fact]
    public void Grace_button_is_enabled_once_then_disabled_after_use()
    {
        var (vm, countdown) = Build(allowExtend: true, timeProvider: new FakeTimeProvider());

        Assert.True(vm.CanExtend);

        vm.ExtendCommand.Execute(null);

        Assert.False(vm.CanExtend);

        countdown.Dispose();
    }

    [Fact]
    public void Shutdown_now_command_invokes_the_execute_now_delegate()
    {
        var invoked = false;
        var (vm, countdown) = Build(executeNow: () =>
        {
            invoked = true;
            return Task.CompletedTask;
        });

        vm.ShutdownNowCommand.Execute(null);

        Assert.True(invoked);
        countdown.Dispose();
    }

    [Fact]
    public void Dry_run_adds_a_marker_to_the_title()
    {
        var (vm, countdown) = Build(dryRun: true);

        Assert.Contains("DRY RUN", vm.TitleText);

        countdown.Dispose();
    }

    [Fact]
    public void Battery_charge_floor_reason_includes_the_current_charge_and_the_floor()
    {
        var reading = new UpsReading { Timestamp = DateTimeOffset.UtcNow, Status = UpsStatus.OB, BatteryCharge = 22 };
        var (vm, countdown) = Build(reason: ShutdownReason.BatteryChargeFloor, readingAtStart: reading);

        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_Reason_BatteryCharge, "22", 30), vm.ReasonText);

        countdown.Dispose();
    }

    [Fact]
    public void Forced_shutdown_reason_has_no_placeholders()
    {
        var (vm, countdown) = Build(reason: ShutdownReason.ForcedShutdown);

        Assert.Equal(Strings.Shutdown_Reason_Fsd, vm.ReasonText);

        countdown.Dispose();
    }

    [Fact]
    public void Battery_status_line_reflects_the_current_reading()
    {
        var reading = new UpsReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = UpsStatus.OB,
            BatteryCharge = 40,
            BatteryRuntime = TimeSpan.FromMinutes(3),
        };
        var (vm, countdown) = Build(readingAtStart: reading);

        var expected = string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_BatteryStatus, "40%", "0:03:00");
        Assert.Equal(expected, vm.BatteryStatusText);

        countdown.Dispose();
    }

    [Fact]
    public void Completing_the_countdown_stops_reporting_it_as_running()
    {
        var time = new FakeTimeProvider();
        var (vm, countdown) = Build(delay: TimeSpan.FromSeconds(1), timeProvider: time);

        Assert.True(vm.IsRunning);
        time.Advance(TimeSpan.FromSeconds(2));

        Assert.False(vm.IsRunning);

        countdown.Dispose();
    }
}
