using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;

namespace CocoNut.App.Services;

/// <summary>
/// Opens and closes the <see cref="ShutdownWindow"/> in step with a <see cref="ShutdownCoordinator"/>: shown
/// (topmost, activated, centered) on <see cref="ShutdownCoordinator.ShutdownPending"/>, closed on
/// <see cref="ShutdownCoordinator.ShutdownCancelled"/>/<see cref="ShutdownCoordinator.ShutdownExecuting"/>. Ports
/// WinNUT's <c>WinNUT.vb</c> opening/closing <c>Shutdown_Gui</c>.
/// </summary>
public sealed class ShutdownWindowController : IDisposable
{
    private readonly ShutdownCoordinator _coordinator;
    private readonly UpsMonitor _monitor;
    private readonly Func<PowerSettings> _powerSettingsProvider;
    private readonly IUiDispatcher _dispatcher;
    private ShutdownWindow? _window;
    private ShutdownViewModel? _viewModel;

    public ShutdownWindowController(
        ShutdownCoordinator coordinator, UpsMonitor monitor, Func<PowerSettings> powerSettingsProvider, IUiDispatcher dispatcher)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _powerSettingsProvider = powerSettingsProvider ?? throw new ArgumentNullException(nameof(powerSettingsProvider));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _coordinator.ShutdownPending += OnShutdownPending;
        _coordinator.ShutdownCancelled += OnShutdownResolved;
        _coordinator.ShutdownExecuting += OnShutdownResolved;
    }

    private void OnShutdownPending(object? sender, ShutdownPendingEventArgs e) => _dispatcher.Post(() =>
    {
        var settings = _powerSettingsProvider();
        var readingAtStart = _monitor.LastReading;

        _viewModel = new ShutdownViewModel(
            e.Reason,
            settings.StopAction,
            e.Countdown,
            settings.AllowExtendDelay,
            _coordinator.DryRun,
            settings.BatteryChargeFloor,
            settings.RuntimeFloorSeconds,
            readingAtStart,
            () => _monitor.LastReading,
            _coordinator.ExecuteNowAsync,
            _coordinator.TryExtend,
            _dispatcher);

        _window = new ShutdownWindow { DataContext = _viewModel };
        _window.Show();
        _window.Activate();
    });

    private void OnShutdownResolved(object? sender, ShutdownReason e) => _dispatcher.Post(CloseWindow);

    private void CloseWindow()
    {
        _window?.Close();
        _window = null;
        _viewModel?.Dispose();
        _viewModel = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _coordinator.ShutdownPending -= OnShutdownPending;
        _coordinator.ShutdownCancelled -= OnShutdownResolved;
        _coordinator.ShutdownExecuting -= OnShutdownResolved;
    }
}
