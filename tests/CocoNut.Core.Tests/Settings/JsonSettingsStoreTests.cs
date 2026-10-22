using CocoNut.Core.Ups;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Actions;
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
    public void Load_WithoutHistorySection_UsesHistoryDefaults()
    {
        // A settings file written before the history feature existed.
        File.WriteAllText(_filePath, """
            { "schemaVersion": 1, "connection": { "host": "old", "port": 3493, "upsName": "ups", "pollIntervalMs": 1000 } }
            """);

        var history = CreateStore().Load().History;

        Assert.True(history.Enabled);
        Assert.Equal(30, history.RetentionDays);
        Assert.Equal(10, history.SampleIntervalSeconds);
    }

    [Fact]
    public void Load_WithNullHistorySection_UsesHistoryDefaults()
    {
        File.WriteAllText(_filePath, """{ "schemaVersion": 1, "history": null }""");

        Assert.Equal(30, CreateStore().Load().History.RetentionDays);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsHistorySettings()
    {
        var store = CreateStore();
        var settings = new AppSettings
        {
            History = new HistorySettings { Enabled = false, RetentionDays = 90, SampleIntervalSeconds = 60 },
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.False(loaded.History.Enabled);
        Assert.Equal(90, loaded.History.RetentionDays);
        Assert.Equal(60, loaded.History.SampleIntervalSeconds);
        Assert.Contains("\"history\"", File.ReadAllText(_filePath));
    }

    [Fact]
    public void SaveAndLoad_RoundTripsTheDisplayStyle_AsAString()
    {
        var store = CreateStore();
        var settings = new AppSettings { General = new GeneralSettings { DisplayStyle = DisplayStyle.Digital } };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal(DisplayStyle.Digital, loaded.General.DisplayStyle);
        Assert.Contains("\"displayStyle\": \"Digital\"", File.ReadAllText(_filePath));
        Assert.Equal(DisplayStyle.Digital, loaded.Clone().General.DisplayStyle);
    }

    [Fact]
    public void Load_WithoutADisplayStyle_DefaultsToGauges()
    {
        // A settings file written before the digital display existed.
        File.WriteAllText(_filePath, """
            { "schemaVersion": 1, "general": { "theme": "Dark", "isFirstRun": false } }
            """);

        var general = CreateStore().Load().General;

        Assert.Equal(DisplayStyle.Gauges, general.DisplayStyle);
        Assert.Equal(AppTheme.Dark, general.Theme);
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

    // ---- actions section ----

    private static AppSettings SettingsWithOneRuleOfEveryActionKind() => new()
    {
        Actions = new ActionsSettings
        {
            Rules =
            [
                new ActionRule
                {
                    Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Name = "Everything",
                    Enabled = false,
                    UpsName = null,
                    DelaySeconds = 45,
                    Trigger = new RuleTrigger
                    {
                        Kind = TriggerKind.ValueAtOrBelow,
                        Flags = UpsStatus.OB | UpsStatus.LB,
                        Metric = RuleMetric.OutputPowerWatts,
                        Threshold = 12.5,
                        OnBatteryOnly = true,
                        DurationSeconds = 7,
                    },
                    Actions =
                    [
                        RuleAction.StopComputer(StopAction.Hibernate),
                        RuleAction.Notify("{rule}: {charge}%"),
                        RuleAction.Log("logged {ups}", LogLevel.Warning),
                        RuleAction.RunCommand("/usr/bin/notify", ["--text", "two words", "\"quoted\""], 90),
                        RuleAction.Webhook("https://hooks.example/x?token=1"),
                    ],
                },
                new ActionRule
                {
                    Id = Guid.Parse("66666666-7777-8888-9999-000000000000"),
                    Name = "Edge",
                    Trigger = new RuleTrigger { Kind = TriggerKind.FlagsBecameInactive, Flags = UpsStatus.OB },
                    Actions = [RuleAction.Notify("power is back")],
                },
            ],
        },
    };

    [Fact]
    public void SaveThenLoad_RoundTripsUserRulesWithEveryActionKind()
    {
        var store = CreateStore();
        var original = SettingsWithOneRuleOfEveryActionKind();

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Actions.Rules.Count);
        var rule = loaded.Actions.Rules[0];
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), rule.Id);
        Assert.Equal("Everything", rule.Name);
        Assert.False(rule.Enabled);
        Assert.Null(rule.UpsName);
        Assert.Equal(45, rule.DelaySeconds);
        Assert.Equal(ShutdownReason.CustomRule, rule.ShutdownReason);
        Assert.Equal(TriggerKind.ValueAtOrBelow, rule.Trigger.Kind);
        Assert.Equal(UpsStatus.OB | UpsStatus.LB, rule.Trigger.Flags);
        Assert.Equal(RuleMetric.OutputPowerWatts, rule.Trigger.Metric);
        Assert.Equal(12.5, rule.Trigger.Threshold);
        Assert.True(rule.Trigger.OnBatteryOnly);
        Assert.Equal(7, rule.Trigger.DurationSeconds);

        Assert.Equal(5, rule.Actions.Count);
        Assert.Equal(ActionKind.StopComputer, rule.Actions[0].Kind);
        Assert.Equal(StopAction.Hibernate, rule.Actions[0].StopAction);
        Assert.Equal(ActionKind.Notify, rule.Actions[1].Kind);
        Assert.Equal("{rule}: {charge}%", rule.Actions[1].Message);
        Assert.Equal(ActionKind.Log, rule.Actions[2].Kind);
        Assert.Equal(LogLevel.Warning, rule.Actions[2].LogLevel);
        Assert.Equal("logged {ups}", rule.Actions[2].Message);
        Assert.Equal(ActionKind.RunCommand, rule.Actions[3].Kind);
        Assert.Equal("/usr/bin/notify", rule.Actions[3].Command);
        Assert.Equal(["--text", "two words", "\"quoted\""], rule.Actions[3].Arguments);
        Assert.Equal(90, rule.Actions[3].TimeoutSeconds);
        Assert.Equal(ActionKind.Webhook, rule.Actions[4].Kind);
        Assert.Equal("https://hooks.example/x?token=1", rule.Actions[4].Url);

        Assert.Equal(TriggerKind.FlagsBecameInactive, loaded.Actions.Rules[1].Trigger.Kind);
    }

    [Fact]
    public void Save_WritesEnumsAsStrings_AndNoStopsComputerMember()
    {
        var store = CreateStore();

        store.Save(SettingsWithOneRuleOfEveryActionKind());

        var json = File.ReadAllText(_filePath);
        Assert.Contains("\"kind\": \"RunCommand\"", json);
        Assert.Contains("\"kind\": \"FlagsBecameInactive\"", json);
        Assert.Contains("\"logLevel\": \"Warning\"", json);
        Assert.Contains("\"stopAction\": \"Hibernate\"", json);
        Assert.Contains("\"shutdownReason\": \"CustomRule\"", json);
        Assert.DoesNotContain("stopsComputer", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_AVersion1FileWithoutAnActionsSection_HasNoRules_AndRoundTrips()
    {
        File.WriteAllText(_filePath, """{ "schemaVersion": 1, "power": { "batteryChargeFloor": 42 } }""");
        var store = CreateStore();

        var loaded = store.Load();

        Assert.NotNull(loaded.Actions);
        Assert.Empty(loaded.Actions.Rules);
        Assert.Equal(42, loaded.Power.BatteryChargeFloor);

        store.Save(loaded);
        var reloaded = store.Load();

        Assert.Empty(reloaded.Actions.Rules);
        Assert.Equal(42, reloaded.Power.BatteryChargeFloor);
        Assert.Equal(1, SettingsFileModel.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "actions": null }""")]
    [InlineData("""{ "schemaVersion": 1, "actions": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "actions": { "rules": null } }""")]
    public void Load_NullOrEmptyActionsSection_BecomesAnEmptyList(string json)
    {
        File.WriteAllText(_filePath, json);

        var loaded = CreateStore().Load();

        Assert.Empty(loaded.Actions.Rules);
    }

    [Fact]
    public void Load_StoredReasonOtherThanCustomRule_IsNormalised_AndNullRulesAreDropped()
    {
        File.WriteAllText(_filePath, """
            { "schemaVersion": 1, "actions": { "rules": [
                null,
                { "id": "11111111-2222-3333-4444-555555555555", "name": "x", "shutdownReason": "ForcedShutdown",
                  "trigger": { "kind": "FlagsActive", "flags": "OB" }, "actions": [ { "kind": "Notify", "message": "m" } ] }
            ] } }
            """);

        var loaded = CreateStore().Load();

        var rule = Assert.Single(loaded.Actions.Rules);
        Assert.Equal(ShutdownReason.CustomRule, rule.ShutdownReason);
        Assert.Equal(UpsStatus.OB, rule.Trigger.Flags);
    }

    [Fact]
    public void Clone_CopiesTheRulesDeeply()
    {
        var original = SettingsWithOneRuleOfEveryActionKind();

        var copy = original.Clone();
        copy.Actions.Rules[0].Actions[3].Arguments.Add("extra");

        Assert.Equal(3, original.Actions.Rules[0].Actions[3].Arguments.Count);
        Assert.Equal(4, copy.Actions.Rules[0].Actions[3].Arguments.Count);
        Assert.Equal("Everything", copy.Actions.Rules[0].Name);
    }

    // ---- UPS info overrides section ----

    [Fact]
    public void SaveThenLoad_RoundTripsTheInfoOverrides_AsCamelCaseJson()
    {
        var store = CreateStore();
        var settings = new AppSettings();
        settings.InfoOverrides.Entries.Add(new UpsInfoOverride { Host = "nut.example", Port = 3494, UpsName = "rack", Manufacturer = "Acme", Model = null });
        settings.InfoOverrides.Entries.Add(new UpsInfoOverride { Host = "other", Port = 3493, UpsName = "ups", Manufacturer = null, Model = "X-1000" });

        store.Save(settings);
        var loaded = store.Load();

        var json = File.ReadAllText(_filePath);
        Assert.Contains("\"infoOverrides\"", json);
        Assert.Contains("\"upsName\": \"rack\"", json);
        Assert.Equal(2, loaded.InfoOverrides.Entries.Count);
        var first = loaded.InfoOverrides.Entries[0];
        Assert.Equal(("nut.example", 3494, "rack", "Acme", null), (first.Host, first.Port, first.UpsName, first.Manufacturer, first.Model));
        var second = loaded.InfoOverrides.Entries[1];
        Assert.Equal(("other", 3493, "ups", null, "X-1000"), (second.Host, second.Port, second.UpsName, second.Manufacturer, second.Model));
    }

    [Fact]
    public void Load_AFileWithoutAnInfoOverridesSection_HasNoEntries_AndRoundTrips()
    {
        File.WriteAllText(_filePath, """{ "schemaVersion": 1, "power": { "batteryChargeFloor": 42 } }""");
        var store = CreateStore();

        var loaded = store.Load();

        Assert.NotNull(loaded.InfoOverrides);
        Assert.Empty(loaded.InfoOverrides.Entries);

        store.Save(loaded);

        Assert.Empty(store.Load().InfoOverrides.Entries);
        Assert.Equal(1, SettingsFileModel.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "infoOverrides": null }""")]
    [InlineData("""{ "schemaVersion": 1, "infoOverrides": {} }""")]
    [InlineData("""{ "schemaVersion": 1, "infoOverrides": { "entries": null } }""")]
    public void Load_NullOrEmptyInfoOverridesSection_BecomesAnEmptyList(string json)
    {
        File.WriteAllText(_filePath, json);

        var loaded = CreateStore().Load();

        Assert.Empty(loaded.InfoOverrides.Entries);
    }

    [Fact]
    public void Load_NullInfoOverrideEntries_AreDropped()
    {
        File.WriteAllText(_filePath, """
            { "schemaVersion": 1, "infoOverrides": { "entries": [
                null,
                { "host": "h", "port": 3493, "upsName": "ups", "model": "X-1000" }
            ] } }
            """);

        var loaded = CreateStore().Load();

        var entry = Assert.Single(loaded.InfoOverrides.Entries);
        Assert.Equal("X-1000", entry.Model);
        Assert.Null(entry.Manufacturer);
    }
}
