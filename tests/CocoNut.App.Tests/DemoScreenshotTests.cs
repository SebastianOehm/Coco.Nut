using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Ups;
using CocoNut.Core.Updates;

namespace CocoNut.App.Tests;

/// <summary>
/// Not a correctness test: renders the real <see cref="MainWindow"/>/<see cref="MainWindowViewModel"/> headlessly,
/// with sample UPS data wired through the fakes, and saves a PNG for the architect's review to the repository's
/// gitignored <c>artifacts/</c> folder. Replaces the WP-F demo screenshot (six bare gauges with no view model)
/// now that the app shell (WP-G) provides the real main window.
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
