using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.Services;

public sealed class FirstRunCoordinatorTests
{
    private static (FirstRunCoordinator Coordinator, InMemorySettingsStore Store, SettingsService Settings, FakeUpsMonitorEvents Monitor)
        Build(AppSettings settings, Func<Task<bool>> showSettings)
    {
        var store = new InMemorySettingsStore { Settings = settings };
        var settingsService = new SettingsService(store, store.Settings);
        var monitor = new FakeUpsMonitorEvents();
        var coordinator = new FirstRunCoordinator(settingsService, monitor, showSettings, NullLogger<FirstRunCoordinator>.Instance);
        return (coordinator, store, settingsService, monitor);
    }

    [Fact]
    public void IsFirstRun_reflects_the_current_settings()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = true;
        var (coordinator, _, _, _) = Build(settings, () => Task.FromResult(true));

        Assert.True(coordinator.IsFirstRun);
    }

    [Fact]
    public async Task RunAsync_does_nothing_when_not_the_first_run()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = false;
        var shown = false;
        var (coordinator, store, _, monitor) = Build(settings, () =>
        {
            shown = true;
            return Task.FromResult(true);
        });

        await coordinator.RunAsync();

        Assert.False(shown);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, monitor.StartCount);
    }

    [Fact]
    public async Task Saving_settings_clears_first_run_and_connects()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = true;
        settings.Connection.Host = "configured-host";
        var (coordinator, store, settingsService, monitor) = Build(settings, () => Task.FromResult(true));

        await coordinator.RunAsync();

        Assert.False(settingsService.Current.General.IsFirstRun);
        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, monitor.StartCount);
        Assert.Equal("configured-host", monitor.Settings!.Host);
    }

    [Fact]
    public async Task Closing_without_saving_leaves_first_run_set_and_does_not_connect()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = true;
        var (coordinator, store, settingsService, monitor) = Build(settings, () => Task.FromResult(false));

        await coordinator.RunAsync();

        Assert.True(settingsService.Current.General.IsFirstRun);
        Assert.Equal(0, store.SaveCount);
        Assert.Equal(0, monitor.StartCount);
    }

    [Fact]
    public async Task An_empty_host_after_saving_does_not_attempt_to_connect()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = true;
        settings.Connection.Host = string.Empty;
        var (coordinator, _, settingsService, monitor) = Build(settings, () => Task.FromResult(true));

        await coordinator.RunAsync();

        Assert.False(settingsService.Current.General.IsFirstRun);
        Assert.Equal(0, monitor.StartCount);
    }

    [Fact]
    public async Task A_failure_starting_the_monitor_does_not_throw()
    {
        var settings = new AppSettings();
        settings.General.IsFirstRun = true;
        settings.Connection.Host = "configured-host";
        var store = new InMemorySettingsStore { Settings = settings };
        var settingsService = new SettingsService(store, store.Settings);
        var monitor = new FakeUpsMonitorEvents { ThrowOnStart = new InvalidOperationException("connect failed") };
        var coordinator = new FirstRunCoordinator(settingsService, monitor, () => Task.FromResult(true), NullLogger<FirstRunCoordinator>.Instance);

        await coordinator.RunAsync(); // Must not throw.

        Assert.False(settingsService.Current.General.IsFirstRun);
    }
}
