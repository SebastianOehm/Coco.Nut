using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CocoNut.Core.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> backed by a single indented JSON file (<see cref="AppPaths.SettingsFile"/> by
/// default). The NUT username/password are encrypted at rest via an <see cref="ISecretProtector"/> and held in
/// clear text only in the returned <see cref="AppSettings"/>.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<JsonSettingsStore> _logger;

    /// <inheritdoc />
    public string FilePath { get; }

    /// <param name="filePath">Full path of the settings file.</param>
    /// <param name="secretProtector">Used to encrypt/decrypt the NUT username and password.</param>
    /// <param name="logger">Optional logger; a no-op logger is used when omitted.</param>
    public JsonSettingsStore(string filePath, ISecretProtector secretProtector, ILogger<JsonSettingsStore>? logger = null)
    {
        FilePath = filePath;
        _secretProtector = secretProtector;
        _logger = logger ?? NullLogger<JsonSettingsStore>.Instance;
    }

    /// <summary>
    /// Convenience constructor for the real application: stores the file at <see cref="AppPaths.SettingsFile"/>
    /// and picks the platform-appropriate <see cref="ISecretProtector"/> via <see cref="SecretProtectorFactory"/>.
    /// </summary>
    public JsonSettingsStore(ILoggerFactory? loggerFactory = null)
        : this(
            AppPaths.SettingsFile,
            SecretProtectorFactory.Create(AppPaths.DataDirectory, loggerFactory),
            loggerFactory?.CreateLogger<JsonSettingsStore>())
    {
    }

    /// <inheritdoc />
    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        string json;
        try
        {
            json = File.ReadAllText(FilePath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read the settings file at {FilePath}; using defaults.", FilePath);
            return new AppSettings();
        }

        SettingsFileModel? model;
        try
        {
            model = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsFileModel);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Settings file at {FilePath} is corrupt; it will be backed up and defaults used.", FilePath);
            QuarantineCorruptFile();
            return new AppSettings();
        }

        if (model is null)
        {
            _logger.LogWarning("Settings file at {FilePath} deserialized to nothing; it will be backed up and defaults used.", FilePath);
            QuarantineCorruptFile();
            return new AppSettings();
        }

        return MapToAppSettings(model);
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        var model = MapToFileModel(settings);
        var json = JsonSerializer.Serialize(model, SettingsJsonContext.Default.SettingsFileModel);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{FilePath}.tmp-{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Renames a corrupt settings file out of the way so it doesn't clobber the next successful save.</summary>
    private void QuarantineCorruptFile()
    {
        try
        {
            var backupPath = $"{FilePath}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(FilePath, backupPath, overwrite: true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to back up the corrupt settings file at {FilePath}.", FilePath);
        }
    }

    private AppSettings MapToAppSettings(SettingsFileModel model)
    {
        var connection = model.Connection;
        return new AppSettings
        {
            Connection = new ConnectionSettings
            {
                Host = connection.Host,
                Port = connection.Port,
                UpsName = connection.UpsName,
                PollIntervalMs = connection.PollIntervalMs,
                Username = UnprotectOrNull(connection.UsernameProtected, "username"),
                Password = UnprotectOrNull(connection.PasswordProtected, "password"),
                AutoReconnect = connection.AutoReconnect,
            },
            Calibration = model.Calibration ?? new CalibrationSettings(),
            Power = model.Power ?? new PowerSettings(),
            General = model.General ?? new GeneralSettings(),
            Logging = model.Logging ?? new LoggingSettings(),
            Update = model.Update ?? new UpdateSettings(),
        };
    }

    private SettingsFileModel MapToFileModel(AppSettings settings) => new()
    {
        SchemaVersion = SettingsFileModel.CurrentSchemaVersion,
        Connection = new ConnectionSettingsFileModel
        {
            Host = settings.Connection.Host,
            Port = settings.Connection.Port,
            UpsName = settings.Connection.UpsName,
            PollIntervalMs = settings.Connection.PollIntervalMs,
            UsernameProtected = ProtectOrNull(settings.Connection.Username),
            PasswordProtected = ProtectOrNull(settings.Connection.Password),
            AutoReconnect = settings.Connection.AutoReconnect,
        },
        Calibration = settings.Calibration,
        Power = settings.Power,
        General = settings.General,
        Logging = settings.Logging,
        Update = settings.Update,
    };

    private string? UnprotectOrNull(string? protectedValue, string fieldName)
    {
        if (string.IsNullOrEmpty(protectedValue))
        {
            return null;
        }

        var plainText = _secretProtector.Unprotect(protectedValue);
        if (plainText is null)
        {
            _logger.LogWarning("Could not decrypt the stored {FieldName}; it will be treated as unset.", fieldName);
        }

        return plainText;
    }

    private string? ProtectOrNull(string? plainText) =>
        string.IsNullOrEmpty(plainText) ? null : _secretProtector.Protect(plainText);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp file; the original settings file was left untouched.
        }
    }
}
