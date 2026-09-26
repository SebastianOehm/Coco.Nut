using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Logging;
using CocoNut.Core.Settings;
using CocoNut.Core.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.App.Tests.Services;

/// <summary>
/// Headless tests of <see cref="WindowNavigator"/>'s singleton-per-open behaviour: a second "show" call while a
/// window is open must reuse the existing instance, and closing it must allow a fresh one afterwards.
/// </summary>
public sealed class WindowNavigatorTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "coconut-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _provider;
    private readonly WindowNavigator _navigator;

    public WindowNavigatorTests()
    {
        // Bridging box: SettingsViewModel needs IWindowNavigator (for its "check now" button), but the navigator
        // itself is not built via this container - it is constructed just below, once, and plugged into the box
        // so a transient view model resolved later (after the box is filled in) sees the same instance.
        var navigatorBox = new WindowNavigatorBox();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISettingsService>(_ => new SettingsService(new InMemorySettingsStore()));
        services.AddSingleton<IUpsMonitorEvents>(_ => new FakeUpsMonitorEvents());
        services.AddSingleton<IPowerActions>(_ => new FakePowerActions());
        services.AddSingleton<IAutoStartService>(_ => new FakeAutoStartService());
        services.AddSingleton<IShellLauncher>(_ => new FakeShellLauncher());
        services.AddSingleton<IUiDispatcher>(_ => new ImmediateUiDispatcher());
        services.AddSingleton(_ => new FileLoggerProvider(_logDirectory));
        services.AddSingleton(_ => new UpdateChecker(new HttpClient()));
        services.AddSingleton<INotificationService>(_ => new FakeNotificationService());
        services.AddSingleton<IWindowNavigator>(_ => navigatorBox.Navigator!);

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<UpsVariablesViewModel>();
        services.AddTransient<AboutViewModel>();

        _provider = services.BuildServiceProvider();
        _navigator = new WindowNavigator(_provider, NullLogger<WindowNavigator>.Instance);
        navigatorBox.Navigator = _navigator;
    }

    private sealed class WindowNavigatorBox
    {
        public WindowNavigator? Navigator { get; set; }
    }

    [AvaloniaFact]
    public async Task ShowSettingsAsync_twice_reuses_the_same_window()
    {
        await _navigator.ShowSettingsAsync();
        var first = GetWindow("_settingsWindow");
        Assert.NotNull(first);

        await _navigator.ShowSettingsAsync();
        var second = GetWindow("_settingsWindow");

        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public async Task Closing_settings_then_reopening_creates_a_new_window()
    {
        await _navigator.ShowSettingsAsync();
        var first = GetWindow("_settingsWindow");
        Assert.NotNull(first);

        first!.Close();
        Assert.Null(GetWindow("_settingsWindow"));

        await _navigator.ShowSettingsAsync();
        var second = GetWindow("_settingsWindow");

        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [AvaloniaFact]
    public async Task ShowUpsVariablesAsync_twice_reuses_the_same_window()
    {
        await _navigator.ShowUpsVariablesAsync();
        var first = GetWindow("_upsVariablesWindow");

        await _navigator.ShowUpsVariablesAsync();
        var second = GetWindow("_upsVariablesWindow");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public async Task ShowAboutAsync_twice_reuses_the_same_window()
    {
        await _navigator.ShowAboutAsync();
        var first = GetWindow("_aboutWindow");

        await _navigator.ShowAboutAsync();
        var second = GetWindow("_aboutWindow");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public async Task ShowUpdateAvailableAsync_twice_reuses_the_same_window()
    {
        var result = UpdateCheckResult.FromRelease(true, new Version(2, 0, 0), new CocoNut.Core.Updates.GitHubRelease { TagName = "v2.0.0" });

        await _navigator.ShowUpdateAvailableAsync(result);
        var first = GetWindow("_updateAvailableWindow");

        await _navigator.ShowUpdateAvailableAsync(result);
        var second = GetWindow("_updateAvailableWindow");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void ShowMainWindow_before_attaching_does_nothing()
    {
        // AttachMainWindow is never called in this fixture; ShowMainWindow must be a safe no-op, not throw.
        _navigator.ShowMainWindow();
    }

    private Window? GetWindow(string fieldName)
    {
        var field = typeof(WindowNavigator).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"WindowNavigator has no field named '{fieldName}'.");
        return (Window?)field.GetValue(_navigator);
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (Directory.Exists(_logDirectory))
        {
            try
            {
                Directory.Delete(_logDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort cleanup.
            }
        }
    }
}
