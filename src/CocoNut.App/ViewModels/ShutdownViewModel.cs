using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the shutdown countdown window, WinNUT's <c>Shutdown_Gui.vb</c> ported onto
/// <see cref="ShutdownCountdown"/>: the reason, the stop action, the countdown text/progress, a live battery
/// status line, and the two buttons ("shut down now" and the one-time grace extension). Takes plain values and
/// delegates rather than a <see cref="ShutdownCoordinator"/> directly, so it is unit testable without one.
/// </summary>
public sealed partial class ShutdownViewModel : ObservableObject, IDisposable
{
    private readonly ShutdownCountdown _countdown;
    private readonly Func<UpsReading?> _currentReadingProvider;
    private readonly Func<Task> _executeNow;
    private readonly Func<bool> _tryExtend;
    private readonly IUiDispatcher _dispatcher;

    /// <param name="reason">Why the stop procedure started (WinNUT: the reason shown above the countdown).</param>
    /// <param name="stopAction">The configured <see cref="Core.Abstractions.StopAction"/>, named in the countdown text.</param>
    /// <param name="countdown">The running countdown to reflect (<see cref="Tick"/>/<see cref="Completed"/>/<see cref="Cancelled"/>).</param>
    /// <param name="allowExtend">Whether the grace button is offered at all (<c>PowerSettings.AllowExtendDelay</c>).</param>
    /// <param name="dryRun">Whether <see cref="ShutdownCoordinator.DryRun"/> is active; shown as a title marker.</param>
    /// <param name="batteryChargeFloor">The configured charge floor, for <see cref="ShutdownReason.BatteryChargeFloor"/>'s reason text.</param>
    /// <param name="runtimeFloorSeconds">The configured runtime floor, for <see cref="ShutdownReason.RuntimeFloor"/>'s reason text.</param>
    /// <param name="readingAtStart">The reading that triggered the stop, used for the fixed reason text's values.</param>
    /// <param name="currentReadingProvider">Polled on every tick for the live battery status line.</param>
    /// <param name="executeNow">Invoked by the "shut down now" button (<see cref="ShutdownCoordinator.ExecuteNowAsync"/>).</param>
    /// <param name="tryExtend">Invoked by the grace button (<see cref="ShutdownCoordinator.TryExtend"/>).</param>
    /// <param name="dispatcher">Marshals <paramref name="countdown"/>'s events (raised off the UI thread) back to it.</param>
    public ShutdownViewModel(
        ShutdownReason reason,
        StopAction stopAction,
        ShutdownCountdown countdown,
        bool allowExtend,
        bool dryRun,
        int batteryChargeFloor,
        int runtimeFloorSeconds,
        UpsReading? readingAtStart,
        Func<UpsReading?> currentReadingProvider,
        Func<Task> executeNow,
        Func<bool> tryExtend,
        IUiDispatcher dispatcher)
    {
        _countdown = countdown ?? throw new ArgumentNullException(nameof(countdown));
        _currentReadingProvider = currentReadingProvider ?? throw new ArgumentNullException(nameof(currentReadingProvider));
        _executeNow = executeNow ?? throw new ArgumentNullException(nameof(executeNow));
        _tryExtend = tryExtend ?? throw new ArgumentNullException(nameof(tryExtend));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        TitleText = dryRun ? $"{Strings.Shutdown_Title} [DRY RUN]" : Strings.Shutdown_Title;
        StopActionText = StopActionFormatter.ToLocalizedText(stopAction);
        ReasonText = BuildReasonText(reason, batteryChargeFloor, runtimeFloorSeconds, readingAtStart);
        CanShowGraceButton = allowExtend;

        _countdown.Tick += OnTick;
        _countdown.Completed += OnCompleted;
        _countdown.Cancelled += OnCancelled;

        UpdateFromCountdown();
        UpdateBatteryStatus();
    }

    [ObservableProperty]
    private string _titleText;

    [ObservableProperty]
    private string _reasonText;

    [ObservableProperty]
    private string _stopActionText;

    [ObservableProperty]
    private string _countdownText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _batteryStatusText = string.Empty;

    /// <summary>Whether the grace button is shown at all (<c>PowerSettings.AllowExtendDelay</c>).</summary>
    [ObservableProperty]
    private bool _canShowGraceButton;

    /// <summary>Whether the grace button is currently enabled (running and not already used once).</summary>
    [ObservableProperty]
    private bool _canExtend;

    /// <summary>While <see langword="true"/>, the window's close button must not close it (WinNUT did the same).</summary>
    [ObservableProperty]
    private bool _isRunning;

    [RelayCommand]
    private Task ShutdownNowAsync() => _executeNow();

    [RelayCommand]
    private void Extend()
    {
        if (_tryExtend())
        {
            UpdateFromCountdown();
            UpdateBatteryStatus();
        }
    }

    private void OnTick(object? sender, TimeSpan remaining) => _dispatcher.Post(() =>
    {
        UpdateFromCountdown();
        UpdateBatteryStatus();
    });

    private void OnCompleted(object? sender, EventArgs e) => _dispatcher.Post(UpdateFromCountdown);

    private void OnCancelled(object? sender, EventArgs e) => _dispatcher.Post(UpdateFromCountdown);

    private void UpdateFromCountdown()
    {
        IsRunning = _countdown.IsRunning;
        CanExtend = _countdown.CanExtend;
        Progress = _countdown.Progress;
        CountdownText = string.Format(
            CultureInfo.CurrentCulture, Strings.Shutdown_Countdown, StopActionText, RuntimeFormatter.FormatCountdown(_countdown.Remaining));
    }

    private void UpdateBatteryStatus()
    {
        var reading = _currentReadingProvider();
        var chargeText = FormatNumber(reading?.BatteryCharge, "%");
        var runtimeText = RuntimeFormatter.FormatRuntime(reading?.BatteryRuntime);
        BatteryStatusText = string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_BatteryStatus, chargeText, runtimeText);
    }

    private static string BuildReasonText(ShutdownReason reason, int chargeFloor, int runtimeFloorSeconds, UpsReading? reading) => reason switch
    {
        ShutdownReason.BatteryChargeFloor => string.Format(
            CultureInfo.CurrentCulture, Strings.Shutdown_Reason_BatteryCharge, FormatNumber(reading?.BatteryCharge), chargeFloor),
        ShutdownReason.RuntimeFloor => string.Format(
            CultureInfo.CurrentCulture, Strings.Shutdown_Reason_Runtime,
            RuntimeFormatter.FormatRuntime(reading?.BatteryRuntime), RuntimeFormatter.FormatHms(TimeSpan.FromSeconds(runtimeFloorSeconds))),
        ShutdownReason.ForcedShutdown => Strings.Shutdown_Reason_Fsd,
        ShutdownReason.UpsLowBattery => Strings.Shutdown_Reason_UpsLowBattery,
        _ => string.Empty,
    };

    private static string FormatNumber(double? value, string suffix = "") =>
        value is { } number ? $"{number.ToString("0.#", CultureInfo.CurrentCulture)}{suffix}" : Strings.Common_Unavailable;

    /// <inheritdoc />
    public void Dispose()
    {
        _countdown.Tick -= OnTick;
        _countdown.Completed -= OnCompleted;
        _countdown.Cancelled -= OnCancelled;
    }
}
