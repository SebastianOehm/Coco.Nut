using System.Collections.ObjectModel;
using System.Globalization;
using CocoNut.App.Controls;
using CocoNut.App.Services;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Settings;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Ups;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the main window: feature parity with WinNUT's <c>WinNUT.vb</c> (UPS info, connection status,
/// status flags, battery, the six gauges, the localized event log) built on <see cref="IUpsMonitorEvents"/> and
/// <see cref="IShutdownEvents"/> so it can be unit tested with fakes instead of a live NUT connection.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>Matches WinNUT's event log line cap (<c>Logger.MaxEvents</c>).</summary>
    public const int MaxEventLogEntries = 200;

    private readonly IUpsMonitorEvents _monitor;
    private readonly IShutdownEvents _shutdownEvents;
    private readonly ISettingsService _settingsService;
    private readonly IWindowNavigator _navigator;
    private readonly INotificationService _notifications;
    private readonly UpdateChecker _updateChecker;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly Action _requestExit;

    private double _powerGaugeMax = PowerGaugeMaxCalculator.DefaultMax;
    private bool _isApplyingSettings;

    public MainWindowViewModel(
        IUpsMonitorEvents monitor,
        IShutdownEvents shutdownEvents,
        ISettingsService settingsService,
        IWindowNavigator navigator,
        INotificationService notifications,
        UpdateChecker updateChecker,
        IUiDispatcher dispatcher,
        TimeProvider timeProvider,
        Action requestExit)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _shutdownEvents = shutdownEvents ?? throw new ArgumentNullException(nameof(shutdownEvents));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));

        _monitor.StateChanged += OnMonitorStateChanged;
        _monitor.ReadingUpdated += OnMonitorReadingUpdated;
        _monitor.StatusChanged += OnMonitorStatusChanged;
        _monitor.ConnectionLost += OnMonitorConnectionLost;

        _shutdownEvents.ShutdownPending += OnShutdownPending;
        _shutdownEvents.ShutdownCancelled += OnShutdownCancelled;
        _shutdownEvents.ShutdownExecuting += OnShutdownExecuting;
        _shutdownEvents.ShutdownFailed += OnShutdownFailed;

        _settingsService.SettingsChanged += OnSettingsChanged;

        ApplySettings(_settingsService.Current);
        HydrateFromMonitor();
    }

    // ---- Gauges -----------------------------------------------------------------------------------------------

    public GaugeViewModel InputVoltageGauge { get; } = new(Strings.Main_Gauge_InputVoltage, "V");
    public GaugeViewModel OutputVoltageGauge { get; } = new(Strings.Main_Gauge_OutputVoltage, "V");
    public GaugeViewModel InputFrequencyGauge { get; } = new(Strings.Main_Gauge_Frequency, "Hz");
    public GaugeViewModel BatteryVoltageGauge { get; } = new(Strings.Main_Gauge_BatteryVoltage, "V");
    public GaugeViewModel LoadGauge { get; } = new(Strings.Main_Gauge_Load, "%");
    public GaugeViewModel PowerGauge { get; } = new(Strings.Main_Gauge_Power, "W");

    // ---- UPS info -----------------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _manufacturerText = Strings.Common_Unavailable;

    [ObservableProperty]
    private string _modelText = Strings.Common_Unavailable;

    [ObservableProperty]
    private string _serialText = Strings.Common_Unavailable;

    [ObservableProperty]
    private string _firmwareText = Strings.Common_Unavailable;

    // ---- Connection state -----------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _connectionStatusText = Strings.Main_Status_NotConnected;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _canConnect = true;

    [ObservableProperty]
    private bool _canDisconnect;

    [ObservableProperty]
    private bool _autoReconnectEnabled;

    // ---- Status flags ---------------------------------------------------------------------------------------------

    public ObservableCollection<string> ActiveStatusFlags { get; } = [];

    [ObservableProperty]
    private bool _indicatorOnLine;

    [ObservableProperty]
    private bool _indicatorOnBattery;

    [ObservableProperty]
    private bool _indicatorLowBattery;

    [ObservableProperty]
    private bool _indicatorOverload;

    // ---- Battery --------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private double _batteryChargePercent;

    [ObservableProperty]
    private string _batteryChargeText = Strings.Common_Unavailable;

    [ObservableProperty]
    private bool _isBatteryChargeEstimated;

    [ObservableProperty]
    private string _batteryRuntimeText = Strings.Main_RuntimeUnknown;

    [ObservableProperty]
    private bool _isBatteryRuntimeEstimated;

    // ---- Event log ------------------------------------------------------------------------------------------------

    public ObservableCollection<EventLogEntry> EventLog { get; } = [];

    // ---- Commands -------------------------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var settings = _settingsService.Current;
        _powerGaugeMax = PowerGaugeMaxCalculator.DefaultMax;
        ApplyPowerGaugeMax();
        await _monitor.StartAsync(settings.Connection, settings.Calibration.InputFrequencyNominal).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task DisconnectAsync() => _monitor.StopAsync();

    [RelayCommand]
    private Task ShowSettingsAsync() => _navigator.ShowSettingsAsync();

    [RelayCommand]
    private Task ShowUpsVariablesAsync() => _navigator.ShowUpsVariablesAsync();

    [RelayCommand]
    private Task ShowAboutAsync() => _navigator.ShowAboutAsync();

    [RelayCommand]
    private void Exit() => _requestExit();

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        AddLogEntry(Strings.Log_Checking);

        var currentVersion = typeof(MainWindowViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var channel = _settingsService.Current.Update.Channel;
        var result = await _updateChecker.CheckAsync(currentVersion, channel).ConfigureAwait(true);

        var updatedSettings = _settingsService.Current.Clone();
        updatedSettings.Update.LastCheck = DateTimeOffset.UtcNow;
        _settingsService.Save(updatedSettings);

        if (result.Error is not null)
        {
            AddLogEntry(string.Format(CultureInfo.CurrentCulture, Strings.Update_CheckFailed, result.Error));
            _notifications.Notify(
                Strings.Notify_Title_Error,
                string.Format(CultureInfo.CurrentCulture, Strings.Update_CheckFailed, result.Error),
                NotificationKind.Error);
            return;
        }

        if (result.IsUpdateAvailable)
        {
            AddLogEntry(string.Format(CultureInfo.CurrentCulture, Strings.Log_UpdateAvailable, result.LatestVersion));
            _notifications.Notify(
                Strings.Notify_Title_Info,
                string.Format(CultureInfo.CurrentCulture, Strings.Update_Message, result.LatestVersion, currentVersion),
                NotificationKind.Info);
            await _navigator.ShowUpdateAvailableAsync(result).ConfigureAwait(true);
        }
        else
        {
            AddLogEntry(Strings.Log_NoUpdateAvailable);
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Update_NoUpdate, NotificationKind.Info);
        }
    }

    partial void OnAutoReconnectEnabledChanged(bool value)
    {
        if (_isApplyingSettings)
        {
            return;
        }

        var updated = _settingsService.Current.Clone();
        updated.Connection.AutoReconnect = value;
        _settingsService.Save(updated);
    }

    // ---- Monitor event handlers (raised on a thread-pool thread; always marshalled to the UI thread) --------------

    private void OnMonitorStateChanged(object? sender, MonitorStateChangedEventArgs e) =>
        _dispatcher.Post(() => HandleStateChanged(e));

    private void OnMonitorReadingUpdated(object? sender, UpsReading reading) =>
        _dispatcher.Post(() => HandleReadingUpdated(reading));

    private void OnMonitorStatusChanged(object? sender, UpsStatusChangedEventArgs e) =>
        _dispatcher.Post(() => HandleStatusChanged(e));

    private void OnMonitorConnectionLost(object? sender, Exception? error) =>
        _dispatcher.Post(() => HandleConnectionLost(error));

    private void OnShutdownPending(object? sender, ShutdownPendingEventArgs e) =>
        _dispatcher.Post(() => AddLogEntry(Strings.Log_ShutdownStart));

    private void OnShutdownCancelled(object? sender, ShutdownReason reason) =>
        _dispatcher.Post(() =>
        {
            AddLogEntry(Strings.Log_ShutdownCancelled);
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Shutdown_Cancelled_Notify, NotificationKind.Info);
        });

    private void OnShutdownExecuting(object? sender, ShutdownReason reason) => _dispatcher.Post(() =>
    {
        var stopActionText = StopActionFormatter.ToLocalizedText(_settingsService.Current.Power.StopAction);
        var template = _shutdownEvents.DryRun ? Strings.Log_ShutdownExecuting_DryRun : Strings.Log_ShutdownExecuting;
        AddLogEntry(string.Format(CultureInfo.CurrentCulture, template, stopActionText));
    });

    private void OnShutdownFailed(object? sender, Exception error) =>
        _dispatcher.Post(() => _notifications.Notify(
            Strings.Notify_Title_Error,
            string.Format(CultureInfo.CurrentCulture, Strings.Shutdown_Failed_Notify, error.Message),
            NotificationKind.Error));

    private void OnSettingsChanged(object? sender, AppSettings settings) => _dispatcher.Post(() => ApplySettings(settings));

    // ---- State projection -------------------------------------------------------------------------------------------

    private void HydrateFromMonitor()
    {
        HandleStateChanged(new MonitorStateChangedEventArgs(_monitor.State, _monitor.State, null));
        if (_monitor.LastReading is { } reading)
        {
            HandleReadingUpdated(reading);
        }
    }

    private void HandleStateChanged(MonitorStateChangedEventArgs e)
    {
        IsConnected = e.NewState == MonitorState.Connected;
        CanConnect = e.NewState == MonitorState.Disconnected;
        CanDisconnect = e.NewState != MonitorState.Disconnected;
        ConnectionStatusText = ConnectionStatusTextBuilder.Build(e.NewState, e.Error);

        var host = _monitor.Settings?.Host;
        var port = _monitor.Settings?.Port;

        switch (e.NewState)
        {
            case MonitorState.Connected:
                AddLogEntry(string.Format(CultureInfo.CurrentCulture, Strings.Log_Connected, host, port));
                if (e.OldState == MonitorState.Reconnecting)
                {
                    // Distinct from the initial connect: the connection had actually dropped and come back.
                    _notifications.Notify(Strings.Notify_Title_Info, Strings.Main_Status_Connected, NotificationKind.Info);
                }

                break;
            case MonitorState.Disconnected:
                if (e.OldState == MonitorState.Connecting && e.Error is not null)
                {
                    AddLogEntry(string.Format(
                        CultureInfo.CurrentCulture, Strings.Log_ConnectionFailed, host, port,
                        ConnectionStatusTextBuilder.DescribeError(e.Error)));
                }

                ResetToNoData();
                break;
            case MonitorState.Reconnecting when e.OldState != MonitorState.Reconnecting:
                AddLogEntry(Strings.Main_Status_Reconnecting);
                break;
        }
    }

    private void HandleReadingUpdated(UpsReading reading)
    {
        if (_monitor.Info is { } info)
        {
            ManufacturerText = string.IsNullOrWhiteSpace(info.Manufacturer) ? Strings.Common_Unavailable : info.Manufacturer;
            ModelText = string.IsNullOrWhiteSpace(info.Model) ? Strings.Common_Unavailable : info.Model;
            SerialText = string.IsNullOrWhiteSpace(info.Serial) ? Strings.Common_Unavailable : info.Serial;
            FirmwareText = string.IsNullOrWhiteSpace(info.Firmware) ? Strings.Common_Unavailable : info.Firmware;
        }

        InputVoltageGauge.Value = reading.InputVoltage;
        OutputVoltageGauge.Value = reading.OutputVoltage;
        InputFrequencyGauge.Value = reading.InputFrequency;
        BatteryVoltageGauge.Value = reading.BatteryVoltage;
        LoadGauge.Value = reading.LoadPercent;
        PowerGauge.Value = reading.OutputPowerWatts;

        _powerGaugeMax = PowerGaugeMaxCalculator.NextMax(_powerGaugeMax, reading);
        ApplyPowerGaugeMax();

        BatteryChargePercent = reading.BatteryCharge ?? 0;
        BatteryChargeText = reading.BatteryCharge is { } charge
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#}%", charge)
            : Strings.Common_Unavailable;
        IsBatteryChargeEstimated = reading.BatteryChargeEstimated && reading.BatteryCharge is not null;

        BatteryRuntimeText = RuntimeFormatter.FormatRuntime(reading.BatteryRuntime);
        IsBatteryRuntimeEstimated = reading.BatteryRuntimeEstimated && reading.BatteryRuntime is not null;

        UpdateStatusIndicators(reading.Status);
    }

    private void HandleStatusChanged(UpsStatusChangedEventArgs e)
    {
        UpdateStatusIndicators(e.Current);

        if (e.NewlyActive.HasFlag(UpsStatus.OB))
        {
            var charge = _monitor.LastReading?.BatteryCharge ?? 0;
            var message = string.Format(CultureInfo.CurrentCulture, Strings.Main_Status_OnBattery, charge.ToString("0.#", CultureInfo.CurrentCulture));
            AddLogEntry(message);
            _notifications.Notify(Strings.Notify_Title_Warning, message, NotificationKind.Warning);
        }

        if (e.NewlyCleared.HasFlag(UpsStatus.OB))
        {
            AddLogEntry(Strings.Main_Status_OnLine);
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Main_Status_OnLine, NotificationKind.Info);
        }

        if (e.NewlyActive.HasFlag(UpsStatus.LB))
        {
            AddLogEntry(Strings.Main_Status_LowBattery);
        }

        if (e.NewlyCleared.HasFlag(UpsStatus.LB))
        {
            AddLogEntry(Strings.Main_Status_BatteryOk);
        }
    }

    private void HandleConnectionLost(Exception? error)
    {
        var host = _monitor.Settings?.Host;
        var port = _monitor.Settings?.Port;
        var message = string.Format(CultureInfo.CurrentCulture, Strings.Main_Status_LostConnection, host, port);
        AddLogEntry(message);
        _notifications.Notify(Strings.Notify_Title_Warning, message, NotificationKind.Warning);
    }

    private void UpdateStatusIndicators(UpsStatus status)
    {
        IndicatorOnLine = status.HasFlag(UpsStatus.OL);
        IndicatorOnBattery = status.HasFlag(UpsStatus.OB);
        IndicatorLowBattery = status.HasFlag(UpsStatus.LB);
        IndicatorOverload = status.HasFlag(UpsStatus.OVER);

        ActiveStatusFlags.Clear();
        foreach (var text in UpsStatusFormatter.ToLocalizedList(status))
        {
            ActiveStatusFlags.Add(text);
        }
    }

    /// <summary>Resets every live value to "no data", per the main window's disconnected state.</summary>
    private void ResetToNoData()
    {
        InputVoltageGauge.Value = null;
        OutputVoltageGauge.Value = null;
        InputFrequencyGauge.Value = null;
        BatteryVoltageGauge.Value = null;
        LoadGauge.Value = null;
        PowerGauge.Value = null;

        BatteryChargePercent = 0;
        BatteryChargeText = Strings.Common_Unavailable;
        IsBatteryChargeEstimated = false;
        BatteryRuntimeText = Strings.Main_RuntimeUnknown;
        IsBatteryRuntimeEstimated = false;

        ManufacturerText = Strings.Common_Unavailable;
        ModelText = Strings.Common_Unavailable;
        SerialText = Strings.Common_Unavailable;
        FirmwareText = Strings.Common_Unavailable;

        UpdateStatusIndicators(UpsStatus.None);
    }

    /// <summary>Adds a new entry at the top of <see cref="EventLog"/>, capping it at <see cref="MaxEventLogEntries"/>.</summary>
    private void AddLogEntry(string message)
    {
        EventLog.Insert(0, new EventLogEntry(_timeProvider.GetLocalNow(), message));
        while (EventLog.Count > MaxEventLogEntries)
        {
            EventLog.RemoveAt(EventLog.Count - 1);
        }
    }

    private void ApplySettings(AppSettings settings)
    {
        _isApplyingSettings = true;
        try
        {
            AutoReconnectEnabled = settings.Connection.AutoReconnect;
            ApplyCalibration(settings.Calibration);
        }
        finally
        {
            _isApplyingSettings = false;
        }
    }

    private void ApplyCalibration(CalibrationSettings calibration)
    {
        InputVoltageGauge.Minimum = calibration.InputVoltageMin;
        InputVoltageGauge.Maximum = calibration.InputVoltageMax;
        InputVoltageGauge.Ranges = GaugePresets.InputVoltage(calibration.InputVoltageMin, calibration.InputVoltageMax);

        OutputVoltageGauge.Minimum = calibration.OutputVoltageMin;
        OutputVoltageGauge.Maximum = calibration.OutputVoltageMax;
        OutputVoltageGauge.Ranges = GaugePresets.OutputVoltage(calibration.OutputVoltageMin, calibration.OutputVoltageMax);

        InputFrequencyGauge.Minimum = calibration.InputFrequencyMin;
        InputFrequencyGauge.Maximum = calibration.InputFrequencyMax;
        InputFrequencyGauge.Ranges = GaugePresets.InputFrequency(calibration.InputFrequencyMin, calibration.InputFrequencyMax);

        BatteryVoltageGauge.Minimum = calibration.BatteryVoltageMin;
        BatteryVoltageGauge.Maximum = calibration.BatteryVoltageMax;
        BatteryVoltageGauge.Ranges = GaugePresets.BatteryVoltage(calibration.BatteryVoltageMin, calibration.BatteryVoltageMax);

        LoadGauge.Minimum = calibration.LoadMin;
        LoadGauge.Maximum = calibration.LoadMax;
        LoadGauge.Ranges = GaugePresets.Load(calibration.LoadMin, calibration.LoadMax);

        ApplyPowerGaugeMax();
    }

    private void ApplyPowerGaugeMax()
    {
        PowerGauge.Minimum = 0;
        PowerGauge.Maximum = _powerGaugeMax;
        PowerGauge.Ranges = GaugePresets.Power(0, _powerGaugeMax);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _monitor.StateChanged -= OnMonitorStateChanged;
        _monitor.ReadingUpdated -= OnMonitorReadingUpdated;
        _monitor.StatusChanged -= OnMonitorStatusChanged;
        _monitor.ConnectionLost -= OnMonitorConnectionLost;

        _shutdownEvents.ShutdownPending -= OnShutdownPending;
        _shutdownEvents.ShutdownCancelled -= OnShutdownCancelled;
        _shutdownEvents.ShutdownExecuting -= OnShutdownExecuting;
        _shutdownEvents.ShutdownFailed -= OnShutdownFailed;

        _settingsService.SettingsChanged -= OnSettingsChanged;
    }
}
