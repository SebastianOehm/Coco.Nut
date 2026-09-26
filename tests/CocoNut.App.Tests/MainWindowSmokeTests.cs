using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Updates;

namespace CocoNut.App.Tests;

/// <summary>Headless smoke test: builds a real <see cref="MainWindow"/> with a real <see cref="MainWindowViewModel"/> (fakes underneath) and renders one frame, catching any wiring/binding mistake that only shows up once the view and view model are put together.</summary>
public class MainWindowSmokeTests
{
    [AvaloniaFact]
    public void MainWindow_with_its_view_model_renders_a_frame_without_throwing()
    {
        var settingsStore = new InMemorySettingsStore();
        var settingsService = new SettingsService(settingsStore, settingsStore.Settings);
        var vm = new MainWindowViewModel(
            new FakeUpsMonitorEvents(),
            new FakeShutdownEvents(),
            settingsService,
            new FakeWindowNavigator(),
            new FakeNotificationService(),
            new UpdateChecker(new HttpClient()),
            new ImmediateUiDispatcher(),
            TimeProvider.System,
            requestExit: () => { });

        var window = new MainWindow(settingsService, requestExit: null) { DataContext = vm };
        window.Show();

        WriteableBitmap? frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        vm.Dispose();
    }
}
