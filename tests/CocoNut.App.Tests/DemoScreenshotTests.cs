using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Logging;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Ups;
using CocoNut.Core.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests;

/// <summary>
/// Not a correctness test: renders real windows/view models headlessly, with sample data wired through the
/// fakes, and saves a PNG of each to the repository's gitignored <c>artifacts/</c> folder for visual review.
/// Replaces the WP-F demo screenshot (six bare gauges with no view model) now that the app shell (WP-G) and the
/// secondary windows (WP-H) provide the real windows.
/// </summary>
public class DemoScreenshotTests
{
    [AvaloniaFact]
    public void MainWindow_with_sample_data_screenshot_is_saved_to_artifacts_mainwindow_png()
    {
        var monitor = new FakeUpsMonitorEvents { Info = new UpsInfo("CyberPower", "CP1500PFCLCD", "SN-000123", "CR01") };
        var settingsStore = new InMemorySettingsStore();
        var settingsService = new SettingsService(settingsStore, settingsStore.Settings);
        var vm = new MainWindowViewModel(
            monitor,
            new FakeShutdownEvents(),
            settingsService,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new UpdateChecker(new HttpClient()),
            new ImmediateUiDispatcher(),
            TimeProvider.System,
            requestExit: () => { });

        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connecting);
        monitor.RaiseStateChanged(MonitorState.Connecting, MonitorState.Connected);
        monitor.RaiseReadingUpdated(new UpsReading
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = UpsStatus.OL | UpsStatus.CHRG,
            BatteryCharge = 92,
            BatteryVoltage = 13.6,
            BatteryRuntime = TimeSpan.FromMinutes(48),
            InputVoltage = 231.4,
            InputFrequency = 50.0,
            OutputVoltage = 229.8,
            LoadPercent = 32,
            OutputPowerWatts = 210,
        });

        var window = new MainWindow(settingsService, requestExit: null) { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        frame.Save(Path.Combine(artifactsDirectory, "mainwindow.png"));

        vm.Dispose();
    }

    [AvaloniaFact]
    public void SettingsWindow_with_sample_data_screenshot_is_saved_to_artifacts_settings_png()
    {
        var settingsStore = new InMemorySettingsStore();
        var settingsService = new SettingsService(settingsStore, settingsStore.Settings);
        var vm = new SettingsViewModel(
            settingsService,
            new FakeUpsMonitorEvents(),
            new FakePowerActions(),
            new FakeAutoStartService(),
            new FakeShellLauncher(),
            new FileLoggerProvider(Path.Combine(Path.GetTempPath(), "coconut-screenshot-logs-" + Guid.NewGuid().ToString("N"))),
            new UpdateChecker(new HttpClient()),
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            NullLogger<SettingsViewModel>.Instance);

        var window = new SettingsWindow { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        frame.Save(Path.Combine(artifactsDirectory, "settings.png"));
    }

    [AvaloniaFact]
    public void UpsVariablesWindow_with_sample_data_screenshot_is_saved_to_artifacts_variables_png()
    {
        var monitor = new FakeUpsMonitorEvents
        {
            Variables =
            [
                new NutVariable("battery.charge", "92", "Battery charge (percent)"),
                new NutVariable("battery.voltage", "13.6", "Battery voltage (V)"),
                new NutVariable("battery.runtime", "2880", "Battery runtime (seconds)"),
                new NutVariable("input.voltage", "231.4", "Input voltage (V)"),
                new NutVariable("input.frequency", "50.0", "Input frequency (Hz)"),
                new NutVariable("output.voltage", "229.8", "Output voltage (V)"),
                new NutVariable("ups.status", "OL CHRG", "UPS status"),
                new NutVariable("ups.mfr", "CyberPower", "UPS manufacturer"),
                new NutVariable("ups.model", "CP1500PFCLCD", "UPS model"),
                new NutVariable("ups.load", "32", "UPS load (percent)"),
            ],
        };
        monitor.RaiseStateChanged(MonitorState.Disconnected, MonitorState.Connected);

        var vm = new UpsVariablesViewModel(monitor, new FakeNotificationService(), new ImmediateUiDispatcher(), NullLogger<UpsVariablesViewModel>.Instance);
        var window = new UpsVariablesWindow { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        string artifactsDirectory = Path.Combine(FindRepositoryRoot(), "artifacts");
        Directory.CreateDirectory(artifactsDirectory);
        frame.Save(Path.Combine(artifactsDirectory, "variables.png"));

        vm.Dispose();
    }

    [AvaloniaFact]
    public void AboutWindow_and_UpdateAvailableWindow_render_a_frame_without_throwing()
    {
        // Smoke test (not a screenshot): catches any binding/wiring mistake in the two simplest secondary
        // windows, which otherwise have no other headless render coverage.
        var aboutWindow = new AboutWindow { DataContext = new AboutViewModel(new FakeShellLauncher()) };
        aboutWindow.Show();
        Assert.NotNull(aboutWindow.CaptureRenderedFrame());

        var result = UpdateCheckResult.FromRelease(true, new Version(1, 2, 0), new CocoNut.Core.Updates.GitHubRelease
        {
            TagName = "v1.2.0",
            Name = "Coco.Nut 1.2.0",
            HtmlUrl = "https://example.invalid/releases/v1.2.0",
            Body = "- Added feature X\n- Fixed bug Y\n- Improved Z",
            PublishedAt = DateTimeOffset.UtcNow,
        });
        var updateWindow = new UpdateAvailableWindow { DataContext = new UpdateAvailableViewModel(result, new Version(1, 1, 0), new FakeShellLauncher()) };
        updateWindow.Show();
        Assert.NotNull(updateWindow.CaptureRenderedFrame());
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CocoNut.sln")))
            {
                return directory.FullName;
            }
        }

        return AppContext.BaseDirectory;
    }
}
