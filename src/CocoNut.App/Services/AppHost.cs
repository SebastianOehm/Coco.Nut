using CocoNut.App.ViewModels;
using CocoNut.Core;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Logging;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Updates;
using CocoNut.Platform.AutoStart;
using CocoNut.Platform.Power;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.Services;

/// <summary>
/// Composition root: builds the app's <see cref="IServiceProvider"/> (settings, logging, the UPS monitor and
/// shutdown coordinator, platform services, view models, …) per <c>docs/PLAN.md</c>'s work package G. Built once
/// in <c>App.axaml.cs</c>'s <c>OnFrameworkInitializationCompleted</c>, before any window is created, and disposed
/// on <c>desktop.Exit</c>.
/// </summary>
public sealed class AppHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private AppHost(ServiceProvider provider) => _provider = provider;

    /// <summary>The built service provider. Resolve everything the app needs through this.</summary>
    public IServiceProvider Services => _provider;

    /// <param name="requestExit">
    /// Invoked by the "Exit" menu items (main window and tray) and forwarded to <see cref="TrayIconController"/>/
    /// <see cref="MainWindowViewModel"/>; wired by <c>App.axaml.cs</c> to the desktop lifetime's real shutdown.
    /// </param>
    public static AppHost Build(Action requestExit)
    {
        ArgumentNullException.ThrowIfNull(requestExit);

        var secretProtector = SecretProtectorFactory.Create(AppPaths.DataDirectory);
        var settingsStore = new JsonSettingsStore(AppPaths.SettingsFile, secretProtector);
        var initialSettings = settingsStore.Load();

        var logBuffer = new LogBuffer();
        var bufferProvider = new BufferLoggerProvider(logBuffer, initialSettings.Logging.MinimumLevel);
        var fileProvider = new FileLoggerProvider(AppPaths.LogDirectory, initialSettings.Logging.LogToFile, initialSettings.Logging.MinimumLevel);

        var services = new ServiceCollection();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(logBuffer);
        services.AddSingleton(bufferProvider);
        services.AddSingleton(fileProvider);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(bufferProvider);
            builder.AddProvider(fileProvider);
        });

        services.AddSingleton<ISettingsStore>(settingsStore);
        services.AddSingleton<ISettingsService>(new SettingsService(settingsStore, initialSettings));

        services.AddSingleton(sp => new UpsMonitor(
            () => new NutClient(sp.GetRequiredService<ILogger<NutClient>>()),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<UpsMonitor>>()));
        services.AddSingleton<IUpsMonitorEvents>(sp => new UpsMonitorAdapter(sp.GetRequiredService<UpsMonitor>()));

        services.AddSingleton(sp => PowerActionsFactory.Create(sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton(sp => new ShutdownCoordinator(
            sp.GetRequiredService<UpsMonitor>(),
            sp.GetRequiredService<IPowerActions>(),
            () => sp.GetRequiredService<ISettingsService>().Current.Power,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<ShutdownCoordinator>>()));
        services.AddSingleton<IShutdownEvents>(sp => new ShutdownEventsAdapter(sp.GetRequiredService<ShutdownCoordinator>()));

        services.AddSingleton(_ => AutoStartServiceFactory.Create());

        services.AddSingleton(_ => new HttpClient());
        services.AddSingleton(sp => new UpdateChecker(sp.GetRequiredService<HttpClient>()));

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<INotificationService, PopupNotificationService>();

        services.AddSingleton<WindowNavigator>();
        services.AddSingleton<IWindowNavigator>(sp => sp.GetRequiredService<WindowNavigator>());

        services.AddSingleton(sp => new TrayIconController(
            sp.GetRequiredService<IUpsMonitorEvents>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IWindowNavigator>(),
            sp.GetRequiredService<IUiDispatcher>(),
            requestExit));

        services.AddSingleton(sp => new ShutdownWindowController(
            sp.GetRequiredService<ShutdownCoordinator>(),
            sp.GetRequiredService<UpsMonitor>(),
            () => sp.GetRequiredService<ISettingsService>().Current.Power,
            sp.GetRequiredService<IUiDispatcher>()));

        services.AddSingleton(sp => new CrashReporter(sp.GetRequiredService<LogBuffer>(), sp.GetRequiredService<ILogger<CrashReporter>>()));

        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<IUpsMonitorEvents>(),
            sp.GetRequiredService<IShutdownEvents>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<IWindowNavigator>(),
            sp.GetRequiredService<INotificationService>(),
            sp.GetRequiredService<UpdateChecker>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<TimeProvider>(),
            requestExit));

        var provider = services.BuildServiceProvider();

        // Keep the loggers' enabled/level and the tray/window in sync with live settings from here on; AppHost
        // itself only reads initialSettings once, above, to build the loggers before the DI container exists.
        var settingsService = provider.GetRequiredService<ISettingsService>();
        settingsService.SettingsChanged += (_, settings) =>
        {
            fileProvider.Enabled = settings.Logging.LogToFile;
            fileProvider.MinimumLevel = settings.Logging.MinimumLevel;
            bufferProvider.MinimumLevel = settings.Logging.MinimumLevel;
        };

        return new AppHost(provider);
    }

    /// <summary>Stops the monitor/coordinator and disposes every disposable singleton, in dependency order.</summary>
    public async ValueTask DisposeAsync()
    {
        (_provider.GetService<TrayIconController>())?.Dispose();
        (_provider.GetService<ShutdownWindowController>())?.Dispose();
        (_provider.GetService<MainWindowViewModel>())?.Dispose();

        if (_provider.GetService<ShutdownCoordinator>() is { } coordinator)
        {
            await coordinator.DisposeAsync().ConfigureAwait(false);
        }

        if (_provider.GetService<UpsMonitor>() is { } monitor)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }

        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}
