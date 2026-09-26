using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using CocoNut.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace CocoNut.Core.Settings;

/// <summary>
/// Imports a WinNUT 2.x <c>user.config</c> (the per-user settings file <c>System.Configuration.ApplicationSettingsBase</c>
/// writes under <c>%LOCALAPPDATA%</c>) into <see cref="AppSettings"/>, so an upgrading user keeps their preferences.
/// WinNUT 1.x's registry/INI preferences are not supported - only the 2.x XML format is.
/// </summary>
public static class WinNutSettingsImporter
{
    private const string SettingsSectionName = "WinNUT_Client.My.MySettings";

    // From WinNUT-Client's SharedAssemblyInfo.vb (AssemblyCompany) and .vbproj (AssemblyName).
    private const string CompanyFolderName = "NUTDotNet";
    private const string AppFolderPattern = "WinNUT-Client*";
    private const string UserConfigFileName = "user.config";

    /// <summary>
    /// Searches the well-known WinNUT 2.x install locations for <c>user.config</c> files:
    /// <c>%LOCALAPPDATA%\NUTDotNet\WinNUT-Client*\&lt;version&gt;\user.config</c> (the folder name after the
    /// company includes a hash suffix .NET appends for non-ClickOnce apps, hence the wildcard). Returns the
    /// newest file first by last-write time. Always empty on non-Windows, since WinNUT never shipped there.
    /// </summary>
    public static IEnumerable<string> FindWinNutConfigFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var companyDirectory = Path.Combine(localAppData, CompanyFolderName);
        if (!Directory.Exists(companyDirectory))
        {
            return [];
        }

        var found = new List<(string Path, DateTime LastWriteTimeUtc)>();
        foreach (var appDirectory in SafeEnumerateDirectories(companyDirectory, AppFolderPattern))
        {
            foreach (var versionDirectory in SafeEnumerateDirectories(appDirectory, "*"))
            {
                var configPath = Path.Combine(versionDirectory, UserConfigFileName);
                if (File.Exists(configPath))
                {
                    found.Add((configPath, File.GetLastWriteTimeUtc(configPath)));
                }
            }
        }

        return found
            .OrderByDescending(entry => entry.LastWriteTimeUtc)
            .Select(entry => entry.Path)
            .ToArray();
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(path, pattern);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Reads a WinNUT 2.x <c>user.config</c> at <paramref name="path"/> and returns an <see cref="AppSettings"/>
    /// with every recognized value applied on top of <paramref name="baseline"/> (a fresh <see cref="AppSettings"/>
    /// with WinNUT-matching defaults when omitted). A setting missing from the file, or one that fails to parse,
    /// keeps the corresponding value from <paramref name="baseline"/>.
    /// </summary>
    /// <param name="path">Full path of the <c>user.config</c> file.</param>
    /// <param name="baseline">Values used when the file does not override them. Not mutated.</param>
    /// <param name="logger">Optional logger for parse failures and skipped secrets; a no-op logger is used when omitted.</param>
    public static AppSettings Import(string path, AppSettings? baseline = null, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var result = (baseline ?? new AppSettings()).Clone();

        var document = XDocument.Load(path);
        var settingsElement = document.Root?.Element("userSettings")?.Element(SettingsSectionName);
        if (settingsElement is null)
        {
            logger.LogWarning(
                "{Path} does not contain a {Section} section; nothing was imported.", path, SettingsSectionName);
            return result;
        }

        var values = settingsElement.Elements("setting")
            .Select(element => (Name: (string?)element.Attribute("name"), Element: element))
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(entry => entry.Name!, entry => entry.Element, StringComparer.Ordinal);

        ImportGeneral(values, result.General);
        ImportLogging(values, result.Logging);
        ImportUpdate(values, result.Update, logger);
        ImportPower(values, result.Power);
        ImportConnection(values, result.Connection, logger);
        ImportCalibration(values, result.Calibration);

        return result;
    }

    private static void ImportGeneral(Dictionary<string, XElement> values, GeneralSettings general)
    {
        general.StartWithOs = GetBool(values, "StartWithWindows") ?? general.StartWithOs;
        general.CloseToTray = GetBool(values, "CloseToTray") ?? general.CloseToTray;
        general.MinimizeOnStart = GetBool(values, "MinimizeOnStart") ?? general.MinimizeOnStart;
        general.MinimizeToTray = GetBool(values, "MinimizeToTray") ?? general.MinimizeToTray;
        general.IsFirstRun = GetBool(values, "IsFirstRun") ?? general.IsFirstRun;
    }

    private static void ImportLogging(Dictionary<string, XElement> values, LoggingSettings logging)
    {
        logging.LogToFile = GetBool(values, "LG_LogToFile") ?? logging.LogToFile;

        var logLevelIndex = GetInt(values, "LG_LogLevel");
        if (logLevelIndex is not null)
        {
            logging.MinimumLevel = MapLogLevel(logLevelIndex.Value) ?? logging.MinimumLevel;
        }
    }

    /// <summary>Maps WinNUT's <c>LG_LogLevel</c> combo box index (<c>Cbx_LogLevel</c>: 0 Notice, 1 Warning/"Alert",
    /// 2 Error, 3 Debug - see <c>WinNUT_Client_Common.LogLvl</c>) to <see cref="MelLogLevel"/>.</summary>
    private static MelLogLevel? MapLogLevel(int index) => index switch
    {
        0 => MelLogLevel.Information, // LOG_NOTICE has no direct MEL equivalent; Information is the closest fit.
        1 => MelLogLevel.Warning, // LOG_WARNING
        2 => MelLogLevel.Error, // LOG_ERROR
        3 => MelLogLevel.Debug, // LOG_DEBUG
        _ => null,
    };

    private static void ImportUpdate(Dictionary<string, XElement> values, UpdateSettings update, ILogger logger)
    {
        update.CheckAtStart = GetBool(values, "UP_CheckAtStart") ?? update.CheckAtStart;

        var delayIndex = GetInt(values, "UP_AutoChkDelay");
        if (delayIndex is not null)
        {
            update.AutoCheckIntervalDays = MapAutoCheckDelay(delayIndex.Value) ?? update.AutoCheckIntervalDays;
        }

        var branchIndex = GetInt(values, "UP_Branch");
        if (branchIndex is not null)
        {
            update.Channel = branchIndex.Value switch
            {
                0 => UpdateChannel.Stable,
                1 => UpdateChannel.PreRelease,
                _ => update.Channel,
            };
        }

        var lastCheckText = GetValue(values, "UP_LastCheck");
        if (!string.IsNullOrWhiteSpace(lastCheckText))
        {
            if (DateTimeOffset.TryParse(lastCheckText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lastCheck))
            {
                update.LastCheck = lastCheck;
            }
            else
            {
                logger.LogWarning("Could not parse WinNUT's UP_LastCheck value {Value}; leaving it unset.", lastCheckText);
            }
        }
    }

    /// <summary>Maps WinNUT's <c>UP_AutoChkDelay</c> combo box index (<c>Cbx_Delay_Verif</c>: 0 Daily, 1 Weekly,
    /// 2 Monthly - see <c>Updater.UpdateUtil.UpdateCheckDelayPassed</c>) to a day count.</summary>
    private static int? MapAutoCheckDelay(int index) => index switch
    {
        0 => 1, // Daily
        1 => 7, // Weekly
        2 => 30, // Monthly (WinNUT used a calendar month; approximated as 30 days here)
        _ => null,
    };

    private static void ImportPower(Dictionary<string, XElement> values, PowerSettings power)
    {
        power.BatteryChargeFloor = GetInt(values, "PW_BattChrgFloor") ?? power.BatteryChargeFloor;
        power.RuntimeFloorSeconds = GetInt(values, "PW_RuntimeFloor") ?? power.RuntimeFloorSeconds;
        power.StopImmediately = GetBool(values, "PW_Immediate") ?? power.StopImmediately;
        power.RespectFsd = GetBool(values, "PW_RespectFSD") ?? power.RespectFsd;

        var stopTypeIndex = GetInt(values, "PW_StopType");
        if (stopTypeIndex is >= 0 and <= 2)
        {
            power.StopAction = (StopAction)stopTypeIndex.Value;
        }

        power.StopDelaySeconds = GetInt(values, "PW_StopDelaySec") ?? power.StopDelaySeconds;
        power.AllowExtendDelay = GetBool(values, "PW_UserExtendStopTimer") ?? power.AllowExtendDelay;
        power.ExtendDelaySeconds = GetInt(values, "PW_ExtendDelaySec") ?? power.ExtendDelaySeconds;
    }

    private static void ImportConnection(Dictionary<string, XElement> values, ConnectionSettings connection, ILogger logger)
    {
        connection.Host = GetValue(values, "NUT_ServerAddress") ?? connection.Host;
        connection.Port = GetInt(values, "NUT_ServerPort") ?? connection.Port;
        connection.UpsName = GetValue(values, "NUT_UPSName") ?? connection.UpsName;
        connection.PollIntervalMs = GetInt(values, "NUT_PollIntervalMsec") ?? connection.PollIntervalMs;
        connection.AutoReconnect = GetBool(values, "NUT_AutoReconnect") ?? connection.AutoReconnect;

        connection.Username = DecryptWinNutProtectedString(GetProtectedRaw(values, "NUT_Username"), logger);
        connection.Password = DecryptWinNutProtectedString(GetProtectedRaw(values, "NUT_Password"), logger);
    }

    private static void ImportCalibration(Dictionary<string, XElement> values, CalibrationSettings calibration)
    {
        calibration.InputVoltageMin = GetInt(values, "CAL_VoltInMin") ?? calibration.InputVoltageMin;
        calibration.InputVoltageMax = GetInt(values, "CAL_VoltInMax") ?? calibration.InputVoltageMax;
        calibration.InputFrequencyNominal = GetInt(values, "CAL_FreqInNom") ?? calibration.InputFrequencyNominal;
        calibration.InputFrequencyMin = GetInt(values, "CAL_FreqInMin") ?? calibration.InputFrequencyMin;
        calibration.InputFrequencyMax = GetInt(values, "CAL_FreqInMax") ?? calibration.InputFrequencyMax;
        calibration.OutputVoltageMin = GetInt(values, "CAL_VoltOutMin") ?? calibration.OutputVoltageMin;
        calibration.OutputVoltageMax = GetInt(values, "CAL_VoltOutMax") ?? calibration.OutputVoltageMax;
        calibration.LoadMin = GetInt(values, "CAL_LoadMin") ?? calibration.LoadMin;
        calibration.LoadMax = GetInt(values, "CAL_LoadMax") ?? calibration.LoadMax;
        calibration.BatteryVoltageMin = GetInt(values, "CAL_BattVMin") ?? calibration.BatteryVoltageMin;
        calibration.BatteryVoltageMax = GetInt(values, "CAL_BattVMax") ?? calibration.BatteryVoltageMax;
    }

    /// <summary>
    /// Attempts to decrypt a WinNUT DPAPI-protected string (<c>SerializedProtectedString</c>: base64 of
    /// <c>ProtectedData.Protect</c> with <see cref="DataProtectionScope.CurrentUser"/>, no entropy, UTF-16LE
    /// bytes). Only possible on Windows and only for the same OS user account that created the file; returns
    /// <see langword="null"/> (never throws) in every other case.
    /// </summary>
    private static string? DecryptWinNutProtectedString(string? protectedBase64, ILogger logger)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            logger.LogInformation(
                "Skipping decryption of a WinNUT-protected credential: DPAPI is only available on Windows.");
            return null;
        }

        return DecryptWithDpapi(protectedBase64, logger);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? DecryptWithDpapi(string protectedBase64, ILogger logger)
    {
        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.Unicode.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            logger.LogWarning(ex, "Failed to decrypt a WinNUT-protected credential; leaving it unset.");
            return null;
        }
    }

    /// <summary>Raw (still base64/DPAPI-protected) value of a setting, unwrapping the XML-serialized form
    /// (<c>&lt;value&gt;&lt;SerializedProtectedString&gt;&lt;ProtectedValue&gt;...</c>) .NET falls back to when a
    /// type has no <see cref="System.ComponentModel.TypeConverter"/>, as well as the plain string form.</summary>
    private static string? GetProtectedRaw(Dictionary<string, XElement> values, string name)
    {
        var valueElement = GetValueElement(values, name);
        if (valueElement is null)
        {
            return null;
        }

        if (!valueElement.HasElements)
        {
            return string.IsNullOrEmpty(valueElement.Value) ? null : valueElement.Value;
        }

        var protectedValueElement = valueElement.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ProtectedValue");
        var raw = protectedValueElement?.Value;
        return string.IsNullOrEmpty(raw) ? null : raw;
    }

    private static XElement? GetValueElement(Dictionary<string, XElement> values, string name) =>
        values.TryGetValue(name, out var settingElement) ? settingElement.Element("value") : null;

    private static string? GetValue(Dictionary<string, XElement> values, string name)
    {
        var text = GetValueElement(values, name)?.Value;
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool? GetBool(Dictionary<string, XElement> values, string name)
    {
        var text = GetValue(values, name);
        return bool.TryParse(text, out var parsed) ? parsed : null;
    }

    private static int? GetInt(Dictionary<string, XElement> values, string name)
    {
        var text = GetValue(values, name);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
