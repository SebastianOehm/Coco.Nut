using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CocoNut.App.Services;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CocoNut.App;

/// <summary>
/// Application entry point. Builds the <see cref="AppHost"/> composition root, applies the configured UI
/// culture before any window is created, enforces a single running instance, wires the global exception
/// handlers, creates the main window and tray icon, and runs the startup sequence (auto-connect, minimize on
/// start, update check) - <c>docs/PLAN.md</c>'s work package G.
/// </summary>
public partial class App : Application, IDisposable
{
    private AppHost? _host;
    private SingleInstanceGuard? _singleInstanceGuard;

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Closing the main window (to tray) must not end the process; only an explicit Exit does.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _singleInstanceGuard = new SingleInstanceGuard();
        if (!_singleInstanceGuard.IsFirstInstance)
        {
            Console.Error.WriteLine("Coco.Nut is already running for this user; exiting.");
            _singleInstanceGuard.Dispose();
            desktop.Shutdown();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        _host = AppHost.Build(() => desktop.Shutdown());
        var services = _host.Services;

        var settingsService = services.GetRequiredService<ISettingsService>();

        // Must happen before any window is created (docs/PLAN.md's localization rules).
        LocalizationBootstrap.Apply(settingsService.Current.General.Language);

        services.GetRequiredService<CrashReporter>().Install();

        var mainWindowViewModel = services.GetRequiredService<MainWindowViewModel>();
        var mainWindow = new MainWindow(settingsService, () => desktop.Shutdown()) { DataContext = mainWindowViewModel };
        desktop.MainWindow = mainWindow;

        services.GetRequiredService<WindowNavigator>().AttachMainWindow(mainWindow);

        var trayIconController = services.GetRequiredService<TrayIconController>();
        trayIconController.Attach(this, mainWindow);
        trayIconController.CheckForUpdatesRequested += (_, _) => mainWindowViewModel.CheckForUpdatesCommand.Execute(null);

        // Safety-critical: the coordinator only evaluates stop conditions once it exists (it subscribes to the
        // monitor in its constructor), so create it explicitly here instead of relying on another service to pull
        // it in. Then instantiate the shutdown-window controller so it watches the coordinator from the start.
        services.GetRequiredService<Core.Shutdown.ShutdownCoordinator>();
        services.GetRequiredService<ShutdownWindowController>();

        _ = RunStartupSequenceAsync(services, settingsService, mainWindow, mainWindowViewModel);

        desktop.Exit += OnExit;

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // The process is exiting either way; a blocking wait here is simpler and safer than a fire-and-forget
        // disposal that might not finish before the process actually terminates. AsTask() first: unlike Task,
        // a ValueTask is not safe to block on directly unless it is already known to be completed.
        _host?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _singleInstanceGuard?.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task RunStartupSequenceAsync(
        IServiceProvider services, ISettingsService settingsService, MainWindow mainWindow, MainWindowViewModel mainWindowViewModel)
    {
        var settings = settingsService.Current;
        var logger = services.GetRequiredService<ILogger<App>>();

        if (!settings.General.MinimizeOnStart)
        {
            mainWindow.Show();
        }

        if (!string.IsNullOrWhiteSpace(settings.Connection.Host))
        {
            try
            {
                var monitor = services.GetRequiredService<IUpsMonitorEvents>();
                await monitor.StartAsync(settings.Connection, settings.Calibration.InputFrequencyNominal).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start the UPS monitor at startup.");
            }
        }

        if (settings.Update.CheckAtStart &&
            UpdateChecker.IsCheckDue(settings.Update.LastCheck, settings.Update.AutoCheckIntervalDays, DateTimeOffset.UtcNow))
        {
            try
            {
                await mainWindowViewModel.CheckForUpdatesCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "The startup update check failed.");
            }
        }
    }
}
