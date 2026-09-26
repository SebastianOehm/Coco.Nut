using System.Globalization;
using CocoNut.App.Services;
using CocoNut.Core.Abstractions;
using CocoNut.Core.Logging;
using CocoNut.Core.Settings;
using CocoNut.Core.Updates;
using CocoNut.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace CocoNut.App.ViewModels;

/// <summary>
/// View model of the Settings window (WinNUT's <c>Pref_Gui.vb</c>, six tabs: Connection, Calibration, Logging,
/// Power, Misc, Update). Edits a private <see cref="AppSettingsExtensions.Clone"/> of <see cref="ISettingsService.Current"/>
/// so nothing is persisted until Save/Apply, validates it with <see cref="AppSettingsValidator"/> on every change,
/// and on a successful save restarts the UPS monitor when connection-relevant settings changed.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IUpsMonitorEvents _monitor;
    private readonly IPowerActions _powerActions;
    private readonly IAutoStartService _autoStartService;
    private readonly IShellLauncher _shellLauncher;
    private readonly FileLoggerProvider _fileLoggerProvider;
    private readonly UpdateChecker _updateChecker;
    private readonly IWindowNavigator _navigator;
    private readonly INotificationService _notifications;
    private readonly ILogger<SettingsViewModel> _logger;

    private readonly AppSettings _working;
    private readonly string? _originalLanguage;
    private ConnectionSettings _lastAppliedConnection;
    private int _lastAppliedNominalFrequency;
    private bool _isLoading;

    public SettingsViewModel(
        ISettingsService settingsService,
        IUpsMonitorEvents monitor,
        IPowerActions powerActions,
        IAutoStartService autoStartService,
        IShellLauncher shellLauncher,
        FileLoggerProvider fileLoggerProvider,
        UpdateChecker updateChecker,
        IWindowNavigator navigator,
        INotificationService notifications,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _powerActions = powerActions ?? throw new ArgumentNullException(nameof(powerActions));
        _autoStartService = autoStartService ?? throw new ArgumentNullException(nameof(autoStartService));
        _shellLauncher = shellLauncher ?? throw new ArgumentNullException(nameof(shellLauncher));
        _fileLoggerProvider = fileLoggerProvider ?? throw new ArgumentNullException(nameof(fileLoggerProvider));
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        _navigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var current = _settingsService.Current;
        _working = current.Clone();
        _lastAppliedConnection = current.Clone().Connection;
        _lastAppliedNominalFrequency = current.Calibration.InputFrequencyNominal;
        _originalLanguage = current.General.Language;

        LogLevelOptions =
        [
            new LogLevelOption(LogLevel.Debug, Strings.Log_Level_Debug),
            new LogLevelOption(LogLevel.Information, Strings.Log_Level_Notice),
            new LogLevelOption(LogLevel.Warning, Strings.Log_Level_Warning),
            new LogLevelOption(LogLevel.Error, Strings.Log_Level_Error),
        ];

        StopActionOptions =
        [.. new[]
            {
                new StopActionOption(StopAction.Shutdown, Strings.StopAction_Shutdown),
                new StopActionOption(StopAction.Suspend, Strings.StopAction_Suspend),
                new StopActionOption(StopAction.Hibernate, Strings.StopAction_Hibernate),
            }
            .Where(option => _powerActions.IsSupported(option.Value))];
        if (StopActionOptions.Count == 0)
        {
            // Every platform's IPowerActions implementation supports at least Shutdown; this is a defensive
            // fallback so the combo box is never left empty (e.g. a test double that reports nothing supported).
            StopActionOptions = [new StopActionOption(StopAction.Shutdown, Strings.StopAction_Shutdown)];
        }

        ChannelOptions =
        [
            new UpdateChannelOption(UpdateChannel.Stable, Strings.UpdateChannel_Stable),
            new UpdateChannelOption(UpdateChannel.PreRelease, Strings.UpdateChannel_PreRelease),
        ];

        CheckIntervalOptions =
        [
            new UpdateIntervalOption(1, Strings.UpdateInterval_Daily),
            new UpdateIntervalOption(7, Strings.UpdateInterval_Weekly),
            new UpdateIntervalOption(30, Strings.UpdateInterval_Monthly),
        ];

        LanguageOptions =
        [
            new LanguageOption(null, Strings.Language_System),
            new LanguageOption("en", Strings.Language_en),
            new LanguageOption("de-DE", Strings.Language_de_DE),
            new LanguageOption("fr-FR", Strings.Language_fr_FR),
            new LanguageOption("ru-RU", Strings.Language_ru_RU),
            new LanguageOption("uk-UA", Strings.Language_uk_UA),
            new LanguageOption("zh-CN", Strings.Language_zh_CN),
            new LanguageOption("zh-TW", Strings.Language_zh_TW),
        ];

        NominalFrequencyOptions = [50, 60];

        IsStartWithSystemSupported = _autoStartService.IsSupported;

        var importFiles = WinNutSettingsImporter.FindWinNutConfigFiles().ToArray();
        _latestImportFile = importFiles.Length > 0 ? importFiles[0] : null;
        ShowImportButton = OperatingSystem.IsWindows() && _latestImportFile is not null;

        LoadFromWorking();
    }

    private readonly string? _latestImportFile;

    /// <summary>Delegate used by the "Delete log files" button to ask for confirmation; wired by whoever shows the
    /// window (it needs the window as the confirmation dialog's owner). Defaults to "never confirmed", so a caller
    /// that forgets to wire it cannot accidentally delete files.</summary>
    public Func<string, string, Task<bool>> ConfirmAsync { get; set; } = (_, _) => Task.FromResult(false);

    /// <summary><see langword="true"/> once at least one Save/Apply has completed successfully. Read by first-run setup.</summary>
    public bool WasSaved { get; private set; }

    /// <summary>Raised once a save/cancel means the window hosting this view model should close itself.</summary>
    public event EventHandler? CloseRequested;

    // ---- Options -----------------------------------------------------------------------------------------------

    public IReadOnlyList<LogLevelOption> LogLevelOptions { get; }

    public IReadOnlyList<StopActionOption> StopActionOptions { get; }

    public IReadOnlyList<UpdateChannelOption> ChannelOptions { get; }

    public IReadOnlyList<UpdateIntervalOption> CheckIntervalOptions { get; }

    public IReadOnlyList<LanguageOption> LanguageOptions { get; }

    public IReadOnlyList<int> NominalFrequencyOptions { get; }

    // ---- Connection ----------------------------------------------------------------------------------------------

    [ObservableProperty]
    private string _host = string.Empty;

    partial void OnHostChanged(string value)
    {
        _working.Connection.Host = value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _port;

    partial void OnPortChanged(decimal value)
    {
        _working.Connection.Port = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private string _upsName = string.Empty;

    partial void OnUpsNameChanged(string value) => _working.Connection.UpsName = value;

    [ObservableProperty]
    private decimal _pollIntervalSeconds;

    partial void OnPollIntervalSecondsChanged(decimal value)
    {
        _working.Connection.PollIntervalMs = (int)Math.Round(value * 1000m, MidpointRounding.AwayFromZero);
        Revalidate();
    }

    [ObservableProperty]
    private string _username = string.Empty;

    partial void OnUsernameChanged(string value) => _working.Connection.Username = string.IsNullOrEmpty(value) ? null : value;

    [ObservableProperty]
    private string _password = string.Empty;

    partial void OnPasswordChanged(string value) => _working.Connection.Password = string.IsNullOrEmpty(value) ? null : value;

    [ObservableProperty]
    private bool _showPassword;

    [ObservableProperty]
    private bool _autoReconnect;

    partial void OnAutoReconnectChanged(bool value) => _working.Connection.AutoReconnect = value;

    [ObservableProperty]
    private string? _hostError;

    [ObservableProperty]
    private string? _portError;

    [ObservableProperty]
    private string? _pollIntervalError;

    // ---- Calibration ---------------------------------------------------------------------------------------------

    [ObservableProperty]
    private decimal _inputVoltageMin;

    partial void OnInputVoltageMinChanged(decimal value)
    {
        _working.Calibration.InputVoltageMin = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _inputVoltageMax;

    partial void OnInputVoltageMaxChanged(decimal value)
    {
        _working.Calibration.InputVoltageMax = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _outputVoltageMin;

    partial void OnOutputVoltageMinChanged(decimal value)
    {
        _working.Calibration.OutputVoltageMin = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _outputVoltageMax;

    partial void OnOutputVoltageMaxChanged(decimal value)
    {
        _working.Calibration.OutputVoltageMax = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _inputFrequencyMin;

    partial void OnInputFrequencyMinChanged(decimal value)
    {
        _working.Calibration.InputFrequencyMin = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _inputFrequencyMax;

    partial void OnInputFrequencyMaxChanged(decimal value)
    {
        _working.Calibration.InputFrequencyMax = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private int _inputFrequencyNominal;

    partial void OnInputFrequencyNominalChanged(int value) => _working.Calibration.InputFrequencyNominal = value;

    [ObservableProperty]
    private decimal _loadMin;

    partial void OnLoadMinChanged(decimal value)
    {
        _working.Calibration.LoadMin = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _loadMax;

    partial void OnLoadMaxChanged(decimal value)
    {
        _working.Calibration.LoadMax = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _batteryVoltageMin;

    partial void OnBatteryVoltageMinChanged(decimal value)
    {
        _working.Calibration.BatteryVoltageMin = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _batteryVoltageMax;

    partial void OnBatteryVoltageMaxChanged(decimal value)
    {
        _working.Calibration.BatteryVoltageMax = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private string? _inputVoltageRangeError;

    [ObservableProperty]
    private string? _outputVoltageRangeError;

    [ObservableProperty]
    private string? _inputFrequencyRangeError;

    [ObservableProperty]
    private string? _loadRangeError;

    [ObservableProperty]
    private string? _batteryVoltageRangeError;

    // ---- Logging -------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _logToFile;

    partial void OnLogToFileChanged(bool value) => _working.Logging.LogToFile = value;

    [ObservableProperty]
    private LogLevelOption _selectedLogLevel = null!;

    partial void OnSelectedLogLevelChanged(LogLevelOption value) => _working.Logging.MinimumLevel = value.Value;

    /// <summary>Whether "Open log file" is enabled: <see cref="FileLoggerProvider.CurrentLogFilePath"/> exists.</summary>
    public bool CanOpenLogFile => File.Exists(_fileLoggerProvider.CurrentLogFilePath);

    [RelayCommand]
    private void OpenLogFile()
    {
        try
        {
            _shellLauncher.OpenFile(_fileLoggerProvider.CurrentLogFilePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Failed to open the log file.");
        }
    }

    [RelayCommand]
    private async Task DeleteLogFilesAsync()
    {
        var confirmed = await ConfirmAsync(Strings.Prefs_Logging_DeleteLog, Strings.Prefs_Logging_DeleteLog_ConfirmMessage).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        _fileLoggerProvider.DeleteLogFiles();
        OnPropertyChanged(nameof(CanOpenLogFile));
    }

    // ---- Power ----------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private decimal _batteryChargeFloor;

    partial void OnBatteryChargeFloorChanged(decimal value)
    {
        _working.Power.BatteryChargeFloor = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private decimal _runtimeFloorSeconds;

    partial void OnRuntimeFloorSecondsChanged(decimal value)
    {
        _working.Power.RuntimeFloorSeconds = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStopDelayEnabled))]
    private bool _stopImmediately;

    partial void OnStopImmediatelyChanged(bool value)
    {
        _working.Power.StopImmediately = value;
        Revalidate();
    }

    /// <summary>WinNUT disabled the stop-delay field while "immediate" is checked (a delay makes no sense then).</summary>
    public bool IsStopDelayEnabled => !StopImmediately;

    [ObservableProperty]
    private bool _respectFsd;

    partial void OnRespectFsdChanged(bool value) => _working.Power.RespectFsd = value;

    [ObservableProperty]
    private StopActionOption _selectedStopAction = null!;

    partial void OnSelectedStopActionChanged(StopActionOption value) => _working.Power.StopAction = value.Value;

    [ObservableProperty]
    private decimal _stopDelaySeconds;

    partial void OnStopDelaySecondsChanged(decimal value)
    {
        _working.Power.StopDelaySeconds = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExtendDelayEnabled))]
    private bool _allowExtendDelay;

    partial void OnAllowExtendDelayChanged(bool value)
    {
        _working.Power.AllowExtendDelay = value;
        Revalidate();
    }

    /// <summary>WinNUT only enabled the grace-time field while the extension is actually allowed.</summary>
    public bool IsExtendDelayEnabled => AllowExtendDelay;

    [ObservableProperty]
    private decimal _extendDelaySeconds;

    partial void OnExtendDelaySecondsChanged(decimal value)
    {
        _working.Power.ExtendDelaySeconds = (int)value;
        Revalidate();
    }

    [ObservableProperty]
    private string? _batteryChargeFloorError;

    [ObservableProperty]
    private string? _runtimeFloorError;

    [ObservableProperty]
    private string? _stopDelayError;

    [ObservableProperty]
    private string? _extendDelayError;

    // ---- Misc -----------------------------------------------------------------------------------------------------

    /// <summary>Whether the "start with system" checkbox is shown at all (<see cref="IAutoStartService.IsSupported"/>).</summary>
    public bool IsStartWithSystemSupported { get; }

    [ObservableProperty]
    private bool _startWithSystem;

    partial void OnStartWithSystemChanged(bool value) => _working.General.StartWithOs = value;

    [ObservableProperty]
    private bool _minimizeOnStart;

    partial void OnMinimizeOnStartChanged(bool value) => _working.General.MinimizeOnStart = value;

    [ObservableProperty]
    private bool _minimizeToTray;

    partial void OnMinimizeToTrayChanged(bool value) => _working.General.MinimizeToTray = value;

    [ObservableProperty]
    private bool _closeToTray;

    partial void OnCloseToTrayChanged(bool value) => _working.General.CloseToTray = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLanguageRestartHintVisible))]
    private LanguageOption _selectedLanguage = null!;

    partial void OnSelectedLanguageChanged(LanguageOption value) => _working.General.Language = value.CultureCode;

    /// <summary>Shown once the selected language differs from the one the app is actually running with.</summary>
    public bool IsLanguageRestartHintVisible => !_isLoading && !string.Equals(SelectedLanguage?.CultureCode, _originalLanguage, StringComparison.Ordinal);

    /// <summary>Whether the "Import WinNUT settings…" button is shown: Windows only, and only if a file was found.</summary>
    public bool ShowImportButton { get; }

    [RelayCommand]
    private void ImportWinNutSettings()
    {
        if (_latestImportFile is null)
        {
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Prefs_Import_NotFound, NotificationKind.Info);
            return;
        }

        try
        {
            var imported = WinNutSettingsImporter.Import(_latestImportFile, _working);
            CopyInto(_working, imported);
            LoadFromWorking();
            _notifications.Notify(
                Strings.Notify_Title_Info,
                string.Format(CultureInfo.CurrentCulture, Strings.Prefs_Import_Success, _latestImportFile),
                NotificationKind.Info);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to import WinNUT settings from {Path}.", _latestImportFile);
            _notifications.Notify(Strings.Notify_Title_Error, Strings.Prefs_Import_NotFound, NotificationKind.Error);
        }
    }

    // ---- Update ---------------------------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _checkAtStart;

    partial void OnCheckAtStartChanged(bool value) => _working.Update.CheckAtStart = value;

    [ObservableProperty]
    private UpdateIntervalOption _selectedCheckInterval = null!;

    partial void OnSelectedCheckIntervalChanged(UpdateIntervalOption value) => _working.Update.AutoCheckIntervalDays = value.Days;

    [ObservableProperty]
    private UpdateChannelOption _selectedChannel = null!;

    partial void OnSelectedChannelChanged(UpdateChannelOption value) => _working.Update.Channel = value.Value;

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        var currentVersion = typeof(SettingsViewModel).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var result = await _updateChecker.CheckAsync(currentVersion, SelectedChannel.Value).ConfigureAwait(true);

        if (result.Error is not null)
        {
            _notifications.Notify(
                Strings.Notify_Title_Error,
                string.Format(CultureInfo.CurrentCulture, Strings.Update_CheckFailed, result.Error),
                NotificationKind.Error);
        }
        else if (result.IsUpdateAvailable)
        {
            await _navigator.ShowUpdateAvailableAsync(result).ConfigureAwait(true);
        }
        else
        {
            _notifications.Notify(Strings.Notify_Title_Info, Strings.Update_NoUpdate, NotificationKind.Info);
        }
    }

    // ---- Validation / Save / Apply / Cancel ------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _hasErrors;

    private bool CanSaveOrApply() => !HasErrors;

    [RelayCommand(CanExecute = nameof(CanSaveOrApply))]
    private async Task SaveAsync()
    {
        await CommitAsync().ConfigureAwait(true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSaveOrApply))]
    private async Task ApplyAsync() => await CommitAsync().ConfigureAwait(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task CommitAsync()
    {
        var toSave = _working.Clone();
        _settingsService.Save(toSave);
        WasSaved = true;

        if (IsStartWithSystemSupported)
        {
            _autoStartService.SetEnabled(toSave.General.StartWithOs, Environment.ProcessPath ?? string.Empty);
        }

        var connectionChanged = !ConnectionEquals(_lastAppliedConnection, toSave.Connection)
            || _lastAppliedNominalFrequency != toSave.Calibration.InputFrequencyNominal;

        if (connectionChanged)
        {
            if (string.IsNullOrWhiteSpace(toSave.Connection.Host))
            {
                await _monitor.StopAsync().ConfigureAwait(true);
            }
            else
            {
                try
                {
                    await _monitor.StartAsync(toSave.Connection, toSave.Calibration.InputFrequencyNominal).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restart the UPS monitor after a settings change.");
                }
            }

            _lastAppliedConnection = toSave.Clone().Connection;
            _lastAppliedNominalFrequency = toSave.Calibration.InputFrequencyNominal;
        }
    }

    private static bool ConnectionEquals(ConnectionSettings a, ConnectionSettings b) =>
        string.Equals(a.Host, b.Host, StringComparison.Ordinal)
        && a.Port == b.Port
        && string.Equals(a.UpsName, b.UpsName, StringComparison.Ordinal)
        && a.PollIntervalMs == b.PollIntervalMs
        && string.Equals(a.Username, b.Username, StringComparison.Ordinal)
        && string.Equals(a.Password, b.Password, StringComparison.Ordinal)
        && a.AutoReconnect == b.AutoReconnect;

    private static void CopyInto(AppSettings target, AppSettings source)
    {
        target.Connection = source.Connection;
        target.Calibration = source.Calibration;
        target.Power = source.Power;
        target.General = source.General;
        target.Logging = source.Logging;
        target.Update = source.Update;
    }

    private void Revalidate()
    {
        var problems = AppSettingsValidator.Validate(_working);

        string? Find(string propertyPath)
        {
            foreach (var problem in problems)
            {
                if (string.Equals(problem.PropertyPath, propertyPath, StringComparison.Ordinal))
                {
                    return Strings.ResourceManager.GetString(problem.ErrorKey, Strings.Culture) ?? problem.ErrorKey;
                }
            }

            return null;
        }

        HostError = Find("Connection.Host");
        PortError = Find("Connection.Port");
        PollIntervalError = Find("Connection.PollIntervalMs");
        InputVoltageRangeError = Find("Calibration.InputVoltageMin");
        InputFrequencyRangeError = Find("Calibration.InputFrequencyMin");
        OutputVoltageRangeError = Find("Calibration.OutputVoltageMin");
        LoadRangeError = Find("Calibration.LoadMin");
        BatteryVoltageRangeError = Find("Calibration.BatteryVoltageMin");
        BatteryChargeFloorError = Find("Power.BatteryChargeFloor");
        RuntimeFloorError = Find("Power.RuntimeFloorSeconds");
        StopDelayError = Find("Power.StopDelaySeconds");
        ExtendDelayError = Find("Power.ExtendDelaySeconds");

        HasErrors = problems.Count > 0;
    }

    private void LoadFromWorking()
    {
        _isLoading = true;
        try
        {
            Host = _working.Connection.Host;
            Port = _working.Connection.Port;
            UpsName = _working.Connection.UpsName;
            PollIntervalSeconds = _working.Connection.PollIntervalMs / 1000m;
            Username = _working.Connection.Username ?? string.Empty;
            Password = _working.Connection.Password ?? string.Empty;
            AutoReconnect = _working.Connection.AutoReconnect;

            InputVoltageMin = _working.Calibration.InputVoltageMin;
            InputVoltageMax = _working.Calibration.InputVoltageMax;
            OutputVoltageMin = _working.Calibration.OutputVoltageMin;
            OutputVoltageMax = _working.Calibration.OutputVoltageMax;
            InputFrequencyMin = _working.Calibration.InputFrequencyMin;
            InputFrequencyMax = _working.Calibration.InputFrequencyMax;
            InputFrequencyNominal = _working.Calibration.InputFrequencyNominal;
            LoadMin = _working.Calibration.LoadMin;
            LoadMax = _working.Calibration.LoadMax;
            BatteryVoltageMin = _working.Calibration.BatteryVoltageMin;
            BatteryVoltageMax = _working.Calibration.BatteryVoltageMax;

            LogToFile = _working.Logging.LogToFile;
            SelectedLogLevel = LogLevelOptions.FirstOrDefault(o => o.Value == _working.Logging.MinimumLevel) ?? LogLevelOptions[0];

            BatteryChargeFloor = _working.Power.BatteryChargeFloor;
            RuntimeFloorSeconds = _working.Power.RuntimeFloorSeconds;
            StopImmediately = _working.Power.StopImmediately;
            RespectFsd = _working.Power.RespectFsd;
            SelectedStopAction = StopActionOptions.FirstOrDefault(o => o.Value == _working.Power.StopAction) ?? StopActionOptions[0];
            StopDelaySeconds = _working.Power.StopDelaySeconds;
            AllowExtendDelay = _working.Power.AllowExtendDelay;
            ExtendDelaySeconds = _working.Power.ExtendDelaySeconds;

            StartWithSystem = _working.General.StartWithOs;
            MinimizeOnStart = _working.General.MinimizeOnStart;
            MinimizeToTray = _working.General.MinimizeToTray;
            CloseToTray = _working.General.CloseToTray;
            SelectedLanguage = LanguageOptions.FirstOrDefault(o => o.CultureCode == _working.General.Language) ?? LanguageOptions[0];

            CheckAtStart = _working.Update.CheckAtStart;
            SelectedCheckInterval = CheckIntervalOptions.FirstOrDefault(o => o.Days == _working.Update.AutoCheckIntervalDays) ?? CheckIntervalOptions[1];
            SelectedChannel = ChannelOptions.FirstOrDefault(o => o.Value == _working.Update.Channel) ?? ChannelOptions[0];
        }
        finally
        {
            _isLoading = false;
        }

        Revalidate();
        OnPropertyChanged(nameof(IsLanguageRestartHintVisible));
    }
}
