using CocoNut.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Settings;

/// <summary>
/// All user preferences. Serialized as JSON by <see cref="ISettingsStore"/>. Defaults mirror WinNUT 2.3
/// (<c>My Project/Settings.settings</c>); the WinNUT setting name is noted on each property.
/// </summary>
public sealed class AppSettings
{
    public ConnectionSettings Connection { get; set; } = new();
    public CalibrationSettings Calibration { get; set; } = new();
    public PowerSettings Power { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();
}

public sealed class ConnectionSettings
{
    /// <summary>NUT_ServerAddress</summary>
    public string Host { get; set; } = "nutserver";
    /// <summary>NUT_ServerPort</summary>
    public int Port { get; set; } = 3493;
    /// <summary>NUT_UPSName</summary>
    public string UpsName { get; set; } = "ups";
    /// <summary>NUT_PollIntervalMsec</summary>
    public int PollIntervalMs { get; set; } = 1000;
    /// <summary>NUT_Username</summary>
    public string? Username { get; set; }
    /// <summary>NUT_Password. Held in clear text in memory only; <see cref="ISettingsStore"/> protects it on disk.</summary>
    public string? Password { get; set; }
    /// <summary>NUT_AutoReconnect</summary>
    public bool AutoReconnect { get; set; }
}

/// <summary>Gauge ranges of the main window.</summary>
public sealed class CalibrationSettings
{
    public int InputVoltageMin { get; set; } = 210;
    public int InputVoltageMax { get; set; } = 270;
    /// <summary>CAL_FreqInNom - also the fallback when the UPS reports no frequency.</summary>
    public int InputFrequencyNominal { get; set; } = 50;
    public int InputFrequencyMin { get; set; } = 40;
    public int InputFrequencyMax { get; set; } = 60;
    public int OutputVoltageMin { get; set; } = 210;
    public int OutputVoltageMax { get; set; } = 270;
    public int LoadMin { get; set; }
    public int LoadMax { get; set; } = 100;
    public int BatteryVoltageMin { get; set; } = 6;
    public int BatteryVoltageMax { get; set; } = 18;
}

public sealed class PowerSettings
{
    /// <summary>PW_BattChrgFloor - stop when battery charge (%) is at or below this value while on battery.</summary>
    public int BatteryChargeFloor { get; set; } = 30;
    /// <summary>PW_RuntimeFloor - stop when remaining runtime (seconds) is at or below this value while on battery.</summary>
    public int RuntimeFloorSeconds { get; set; } = 120;
    /// <summary>PW_Immediate - skip the countdown window.</summary>
    public bool StopImmediately { get; set; }
    /// <summary>PW_RespectFSD - start the stop procedure when the server sets FSD.</summary>
    public bool RespectFsd { get; set; }
    /// <summary>PW_StopType</summary>
    public StopAction StopAction { get; set; } = StopAction.Shutdown;
    /// <summary>PW_StopDelaySec - countdown length.</summary>
    public int StopDelaySeconds { get; set; } = 15;
    /// <summary>PW_UserExtendStopTimer - allow the user to extend the countdown once.</summary>
    public bool AllowExtendDelay { get; set; }
    /// <summary>PW_ExtendDelaySec</summary>
    public int ExtendDelaySeconds { get; set; } = 15;
}

public sealed class GeneralSettings
{
    /// <summary>StartWithWindows</summary>
    public bool StartWithOs { get; set; }
    public bool CloseToTray { get; set; }
    public bool MinimizeOnStart { get; set; }
    public bool MinimizeToTray { get; set; }
    /// <summary>UI culture name (e.g. "de-DE"); <see langword="null"/> = follow the operating system.</summary>
    public string? Language { get; set; }
    /// <summary>Light/dark appearance (new in Coco.Nut).</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;
    /// <summary>IsFirstRun</summary>
    public bool IsFirstRun { get; set; } = true;
}

/// <summary>Light/dark appearance of the UI.</summary>
public enum AppTheme
{
    /// <summary>Follow the operating system.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed class LoggingSettings
{
    /// <summary>LG_LogToFile</summary>
    public bool LogToFile { get; set; }
    /// <summary>LG_LogLevel (WinNUT: 0 Notice, 1 Warning, 2 Error, 3 Debug).</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

public enum UpdateChannel
{
    Stable = 0,
    PreRelease = 1,
}

public sealed class UpdateSettings
{
    /// <summary>UP_CheckAtStart</summary>
    public bool CheckAtStart { get; set; }
    /// <summary>UP_AutoChkDelay</summary>
    public int AutoCheckIntervalDays { get; set; } = 7;
    /// <summary>UP_Branch</summary>
    public UpdateChannel Channel { get; set; } = UpdateChannel.Stable;
    /// <summary>UP_LastCheck</summary>
    public DateTimeOffset? LastCheck { get; set; }
}
