using CocoNut.Core.Abstractions;
using CocoNut.Core.Settings;
using CocoNut.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _filePath;
    private readonly ISecretProtector _protector;

    public JsonSettingsStoreTests()
    {
        _filePath = Path.Combine(_temp.Path, "settings.json");
        _protector = new AesKeyFileSecretProtector(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    private JsonSettingsStore CreateStore() => new(_filePath, _protector);

    [Fact]
    public void Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal(new AppSettings().Connection.Host, settings.Connection.Host);
        Assert.Equal(3493, settings.Connection.Port);
        Assert.Null(settings.Connection.Username);
        Assert.Null(settings.Connection.Password);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllSections()
    {
        var store = CreateStore();
        var settings = new AppSettings
        {
            Connection = new ConnectionSettings
            {
                Host = "ups.example.org",
                Port = 4242,
                UpsName = "office-ups",
                PollIntervalMs = 2500,
                Username = "monuser",
                Password = "sup3r-secret",
                AutoReconnect = true,
            },
            Calibration = new CalibrationSettings
            {
                InputVoltageMin = 200,
                InputVoltageMax = 260,
                InputFrequencyNominal = 60,
                InputFrequencyMin = 55,
                InputFrequencyMax = 65,
                OutputVoltageMin = 205,
                OutputVoltageMax = 255,
                LoadMin = 5,
                LoadMax = 95,
                BatteryVoltageMin = 10,
                BatteryVoltageMax = 14,
            },
            Power = new PowerSettings
            {
                BatteryChargeFloor = 40,
                RuntimeFloorSeconds = 300,
                StopImmediately = true,
                RespectFsd = true,
                StopAction = StopAction.Hibernate,
                StopDelaySeconds = 30,
                AllowExtendDelay = true,
                ExtendDelaySeconds = 45,
            },
            General = new GeneralSettings
            {
                StartWithOs = true,
                CloseToTray = true,
                MinimizeOnStart = true,
                MinimizeToTray = true,
                Language = "de-DE",
                IsFirstRun = false,
            },
            Logging = new LoggingSettings
            {
                LogToFile = true,
                MinimumLevel = LogLevel.Debug,
            },
            Update = new UpdateSettings
            {
                CheckAtStart = true,
                AutoCheckIntervalDays = 14,
                Channel = UpdateChannel.PreRelease,
                LastCheck = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(settings.Connection.Host, loaded.Connection.Host);
        Assert.Equal(settings.Connection.Port, loaded.Connection.Port);
        Assert.Equal(settings.Connection.UpsName, loaded.Connection.UpsName);
        Assert.Equal(settings.Connection.PollIntervalMs, loaded.Connection.PollIntervalMs);
        Assert.Equal(settings.Connection.Username, loaded.Connection.Username);
        Assert.Equal(settings.Connection.Password, loaded.Connection.Password);
        Assert.Equal(settings.Connection.AutoReconnect, loaded.Connection.AutoReconnect);

        Assert.Equal(settings.Calibration.InputVoltageMin, loaded.Calibration.InputVoltageMin);
        Assert.Equal(settings.Calibration.BatteryVoltageMax, loaded.Calibration.BatteryVoltageMax);

        Assert.Equal(settings.Power.StopAction, loaded.Power.StopAction);
        Assert.Equal(settings.Power.ExtendDelaySeconds, loaded.Power.ExtendDelaySeconds);

        Assert.Equal(settings.General.Language, loaded.General.Language);
        Assert.Equal(settings.General.IsFirstRun, loaded.General.IsFirstRun);

        Assert.Equal(settings.Logging.MinimumLevel, loaded.Logging.MinimumLevel);
        Assert.Equal(settings.Update.Channel, loaded.Update.Channel);
        Assert.Equal(settings.Update.LastCheck, loaded.Update.LastCheck);
    }

    [Fact]
    public void Save_NeverWritesThePasswordInPlainTextToDisk()
    {
        var store = CreateStore();
        var settings = new AppSettings
        {
            Connection = new ConnectionSettings { Username = "monuser", Password = "sup3r-secret-marker" },
        };

        store.Save(settings);
        var fileText = File.ReadAllText(_filePath);

        Assert.DoesNotContain("sup3r-secret-marker", fileText, StringComparison.Ordinal);
        Assert.DoesNotContain("monuser", fileText, StringComparison.Ordinal);
    }

    [Fact]
    public void Save_WritesASchemaVersion()
    {
        var store = CreateStore();
        store.Save(new AppSettings());

        var fileText = File.ReadAllText(_filePath);

        Assert.Contains("schemaVersion", fileText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_WritesAtomically_NoLeftoverTempFile()
    {
        var store = CreateStore();
        store.Save(new AppSettings());

        var leftovers = Directory.GetFiles(_temp.Path, "*.tmp-*");
        Assert.Empty(leftovers);
    }

    [Fact]
    public void Load_WhenFileIsCorrupt_BacksItUpAndReturnsDefaults()
    {
        File.WriteAllText(_filePath, "{ this is not valid json ");
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal(new AppSettings().Connection.Port, settings.Connection.Port);
        Assert.False(File.Exists(_filePath));
        var backups = Directory.GetFiles(_temp.Path, "settings.json.corrupt-*");
        Assert.Single(backups);
        Assert.Contains("this is not valid json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Load_WithUnknownProperties_IgnoresThemAndLoadsTheRest()
    {
        File.WriteAllText(_filePath, """
            {
              "schemaVersion": 1,
              "somethingFromTheFuture": { "nested": true },
              "connection": { "host": "known-host", "port": 3493, "upsName": "ups", "pollIntervalMs": 1000, "autoReconnect": false },
              "unknownTopLevelField": 42
            }
            """);
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal("known-host", settings.Connection.Host);
    }

    [Fact]
    public void Load_WithMissingSections_UsesDefaultsForThem()
    {
        File.WriteAllText(_filePath, """
            {
              "schemaVersion": 1,
              "connection": { "host": "only-connection-set", "port": 3493, "upsName": "ups", "pollIntervalMs": 1000, "autoReconnect": false }
            }
            """);
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal("only-connection-set", settings.Connection.Host);
        Assert.Equal(new PowerSettings().BatteryChargeFloor, settings.Power.BatteryChargeFloor);
        Assert.Equal(new CalibrationSettings().InputVoltageMax, settings.Calibration.InputVoltageMax);
        Assert.Equal(new UpdateSettings().AutoCheckIntervalDays, settings.Update.AutoCheckIntervalDays);
    }

    [Fact]
    public void Load_WithNullSections_UsesDefaultsForThem()
    {
        File.WriteAllText(_filePath, """
            { "schemaVersion": 1, "connection": null, "power": null, "general": null }
            """);
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal(new ConnectionSettings().Host, settings.Connection.Host);
        Assert.Equal(new PowerSettings().BatteryChargeFloor, settings.Power.BatteryChargeFloor);
        Assert.NotNull(settings.General);
    }

    [Fact]
    public void Load_WhenSecretCannotBeDecrypted_LeavesItNullInsteadOfThrowing()
    {
        var store = CreateStore();
        store.Save(new AppSettings { Connection = new ConnectionSettings { Password = "will-be-undecryptable" } });

        // Load the same file with a different key (simulating a lost key/machine change): decryption must fail
        // gracefully rather than throw, and the value becomes null.
        using var otherKeyDir = new TempDirectory();
        var storeWithDifferentKey = new JsonSettingsStore(_filePath, new AesKeyFileSecretProtector(otherKeyDir.Path));

        var settings = storeWithDifferentKey.Load();

        Assert.Null(settings.Connection.Password);
    }

    [Fact]
    public void Save_WhenUsernameAndPasswordAreNull_StoresNoProtectedFields()
    {
        var store = CreateStore();
        store.Save(new AppSettings { Connection = new ConnectionSettings { Username = null, Password = null } });

        var loaded = store.Load();

        Assert.Null(loaded.Connection.Username);
        Assert.Null(loaded.Connection.Password);
    }
}
