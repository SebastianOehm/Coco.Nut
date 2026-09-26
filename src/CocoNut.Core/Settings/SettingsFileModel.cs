namespace CocoNut.Core.Settings;

/// <summary>
/// On-disk shape of the settings file: <see cref="AppSettings"/> wrapped in an envelope that carries a
/// <see cref="SchemaVersion"/> (for future migrations) and stores the NUT username/password protected instead
/// of in clear text. Kept separate from <see cref="AppSettings"/> so that contract stays free of persistence
/// concerns.
/// </summary>
public sealed class SettingsFileModel
{
    /// <summary>Schema version written by this build. Bump when the on-disk shape changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Version of the file as it was read from disk (or <see cref="CurrentSchemaVersion"/> when new).</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public ConnectionSettingsFileModel Connection { get; set; } = new();
    public CalibrationSettings Calibration { get; set; } = new();
    public PowerSettings Power { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public UpdateSettings Update { get; set; } = new();
}

/// <summary>Mirrors <see cref="ConnectionSettings"/> but stores the username/password protected on disk.</summary>
public sealed class ConnectionSettingsFileModel
{
    public string Host { get; set; } = "nutserver";
    public int Port { get; set; } = 3493;
    public string UpsName { get; set; } = "ups";
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>Result of <see cref="ISecretProtector.Protect"/> on <see cref="ConnectionSettings.Username"/>.</summary>
    public string? UsernameProtected { get; set; }

    /// <summary>Result of <see cref="ISecretProtector.Protect"/> on <see cref="ConnectionSettings.Password"/>.</summary>
    public string? PasswordProtected { get; set; }

    public bool AutoReconnect { get; set; }
}
