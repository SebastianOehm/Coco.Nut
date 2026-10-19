using CocoNut.Core.Ups;
using CocoNut.Core.Shutdown;
using CocoNut.Core.Actions;
using CocoNut.Core.Settings;

namespace CocoNut.Core.Tests.Settings;

public class AppSettingsValidatorTests
{
    [Fact]
    public void Validate_DefaultSettings_HasNoProblems()
    {
        var problems = AppSettingsValidator.Validate(new AppSettings());

        Assert.Empty(problems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Validate_PortOutOfRange_ReportsProblem(int port)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { Port = port } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Connection.Port" && p.ErrorKey == "Validation_PortRange");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    [InlineData(3493)]
    public void Validate_PortInRange_ReportsNoPortProblem(int port)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { Port = port } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Connection.Port");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyHost_ReportsProblem(string? host)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { Host = host! } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Connection.Host" && p.ErrorKey == "Validation_HostRequired");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("[::1]")]
    [InlineData("fe80::1")]
    [InlineData("nas.local")]
    [InlineData("ups-server")]
    [InlineData("localhost")]
    [InlineData("  nas.local  ")]
    public void Validate_ValidHost_ReportsNoHostProblem(string host)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { Host = host } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Connection.Host");
    }

    [Theory]
    [InlineData("http://host")]
    [InlineData("host:3493")]
    [InlineData("my host")]
    [InlineData("host/")]
    [InlineData("-")]
    public void Validate_InvalidHost_ReportsOnlyHostInvalid(string host)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { Host = host } };

        var problems = AppSettingsValidator.Validate(settings);

        var hostProblem = Assert.Single(problems, p => p.PropertyPath == "Connection.Host");
        Assert.Equal("Validation_HostInvalid", hostProblem.ErrorKey);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100_001)]
    public void Validate_PollIntervalOutOfRange_ReportsProblem(int pollIntervalMs)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { PollIntervalMs = pollIntervalMs } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Connection.PollIntervalMs");
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void Validate_PollIntervalInRange_ReportsNoProblem(int pollIntervalMs)
    {
        var settings = new AppSettings { Connection = new ConnectionSettings { PollIntervalMs = pollIntervalMs } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Connection.PollIntervalMs");
    }

    [Fact]
    public void Validate_CalibrationMinNotLessThanMax_ReportsProblemForEveryPair()
    {
        var settings = new AppSettings
        {
            Calibration = new CalibrationSettings
            {
                InputVoltageMin = 250,
                InputVoltageMax = 250,
                InputFrequencyMin = 60,
                InputFrequencyMax = 50,
                OutputVoltageMin = 250,
                OutputVoltageMax = 250,
                LoadMin = 100,
                LoadMax = 0,
                BatteryVoltageMin = 18,
                BatteryVoltageMax = 6,
            },
        };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Calibration.InputVoltageMin");
        Assert.Contains(problems, p => p.PropertyPath == "Calibration.InputFrequencyMin");
        Assert.Contains(problems, p => p.PropertyPath == "Calibration.OutputVoltageMin");
        Assert.Contains(problems, p => p.PropertyPath == "Calibration.LoadMin");
        Assert.Contains(problems, p => p.PropertyPath == "Calibration.BatteryVoltageMin");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_ChargeFloorOutOfRange_ReportsProblem(int floor)
    {
        var settings = new AppSettings { Power = new PowerSettings { BatteryChargeFloor = floor } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Power.BatteryChargeFloor");
    }

    [Fact]
    public void Validate_RuntimeFloorNegative_ReportsProblem()
    {
        var settings = new AppSettings { Power = new PowerSettings { RuntimeFloorSeconds = -1 } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Power.RuntimeFloorSeconds");
    }

    [Fact]
    public void Validate_RuntimeFloorZero_ReportsNoProblem()
    {
        var settings = new AppSettings { Power = new PowerSettings { RuntimeFloorSeconds = 0 } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Power.RuntimeFloorSeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void Validate_StopDelayOutOfRange_ReportsProblem(int delay)
    {
        var settings = new AppSettings { Power = new PowerSettings { StopDelaySeconds = delay } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Power.StopDelaySeconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3601)]
    public void Validate_ExtendDelayOutOfRange_ReportsProblem(int delay)
    {
        var settings = new AppSettings { Power = new PowerSettings { ExtendDelaySeconds = delay } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "Power.ExtendDelaySeconds");
    }

    [Fact]
    public void Validate_DelayOfOne_ReportsNoProblem()
    {
        var settings = new AppSettings
        {
            Power = new PowerSettings { StopDelaySeconds = 1, ExtendDelaySeconds = 1 },
        };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Power.StopDelaySeconds");
        Assert.DoesNotContain(problems, p => p.PropertyPath == "Power.ExtendDelaySeconds");
    }
}

public class AppSettingsCloneTests
{
    [Fact]
    public void Clone_ProducesEqualButIndependentCopy()
    {
        var original = new AppSettings
        {
            Connection = new ConnectionSettings { Host = "original-host", Username = "user", Password = "pass" },
        };

        var clone = original.Clone();
        clone.Connection.Host = "mutated-host";
        clone.Connection.Username = "mutated-user";

        Assert.Equal("original-host", original.Connection.Host);
        Assert.Equal("user", original.Connection.Username);
        Assert.Equal("mutated-host", clone.Connection.Host);
        Assert.NotSame(original.Connection, clone.Connection);
        Assert.NotSame(original.Calibration, clone.Calibration);
    }

    [Fact]
    public void Clone_PreservesAllValues()
    {
        var original = new AppSettings();
        original.Update.Channel = UpdateChannel.PreRelease;
        original.Logging.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Debug;

        var clone = original.Clone();

        Assert.Equal(UpdateChannel.PreRelease, clone.Update.Channel);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, clone.Logging.MinimumLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public void Validate_HistoryRetentionOutOfRange_ReportsProblem(int days)
    {
        var settings = new AppSettings { History = new HistorySettings { RetentionDays = days } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "History.RetentionDays" && p.ErrorKey == "Validation_HistoryRetentionRange");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(3650)]
    public void Validate_HistoryRetentionInRange_ReportsNoProblem(int days)
    {
        var settings = new AppSettings { History = new HistorySettings { RetentionDays = days } };

        Assert.DoesNotContain(AppSettingsValidator.Validate(settings), p => p.PropertyPath == "History.RetentionDays");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3601)]
    public void Validate_HistorySampleIntervalOutOfRange_ReportsProblem(int seconds)
    {
        var settings = new AppSettings { History = new HistorySettings { SampleIntervalSeconds = seconds } };

        var problems = AppSettingsValidator.Validate(settings);

        Assert.Contains(problems, p => p.PropertyPath == "History.SampleIntervalSeconds" && p.ErrorKey == "Validation_HistorySampleIntervalRange");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(3600)]
    public void Validate_HistorySampleIntervalInRange_ReportsNoProblem(int seconds)
    {
        var settings = new AppSettings { History = new HistorySettings { SampleIntervalSeconds = seconds } };

        Assert.DoesNotContain(AppSettingsValidator.Validate(settings), p => p.PropertyPath == "History.SampleIntervalSeconds");
    }

    // ---- user rules ----

    private static ActionRule ValidRule(string name = "rule") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Trigger = new RuleTrigger { Kind = TriggerKind.FlagsActive, Flags = UpsStatus.OB },
        Actions = [RuleAction.Log("hello")],
    };

    private static IReadOnlyList<SettingsValidationProblem> ValidateRules(params ActionRule[] rules) =>
        AppSettingsValidator.Validate(new AppSettings { Actions = new ActionsSettings { Rules = [.. rules] } });

    private static void AssertProblem(IReadOnlyList<SettingsValidationProblem> problems, string path, string key) =>
        Assert.Contains(problems, p => p.PropertyPath == path && p.ErrorKey == key);

    [Fact]
    public void Validate_ValidUserRule_HasNoProblems() => Assert.Empty(ValidateRules(ValidRule()));

    [Fact]
    public void Validate_RuleWithoutName_ReportsProblem()
    {
        var rule = ValidRule(" ");

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].Name", "Validation_RuleNameRequired");
    }

    [Fact]
    public void Validate_RuleIds_MustBeNonEmptyUniqueAndNotBuiltIn()
    {
        var duplicate = ValidRule("a");
        var twin = ValidRule("b");
        twin.Id = duplicate.Id;
        var empty = ValidRule("c");
        empty.Id = Guid.Empty;
        var builtIn = ValidRule("d");
        builtIn.Id = DefaultRules.RuntimeFloorId;

        var problems = ValidateRules(duplicate, twin, empty, builtIn);

        Assert.DoesNotContain(problems, p => p.PropertyPath == "Actions.Rules[0].Id");
        AssertProblem(problems, "Actions.Rules[1].Id", "Validation_RuleIdInvalid");
        AssertProblem(problems, "Actions.Rules[2].Id", "Validation_RuleIdInvalid");
        AssertProblem(problems, "Actions.Rules[3].Id", "Validation_RuleIdInvalid");
    }

    [Fact]
    public void Validate_RuleWithAnotherReasonThanCustomRule_ReportsProblem()
    {
        var rule = ValidRule();
        rule.ShutdownReason = ShutdownReason.BatteryChargeFloor;

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].ShutdownReason", "Validation_RuleReasonInvalid");
    }

    [Fact]
    public void Validate_RuleWithoutActions_ReportsProblem()
    {
        var rule = ValidRule();
        rule.Actions = [];

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].Actions", "Validation_RuleActionsRequired");
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    public void Validate_RuleDelay_MustBeWithinZeroAndADay(int seconds, bool valid)
    {
        var rule = ValidRule();
        rule.DelaySeconds = seconds;

        Assert.Equal(valid, !ValidateRules(rule).Any(p => p.ErrorKey == "Validation_RuleDelayRange"));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(86400, true)]
    [InlineData(86401, false)]
    public void Validate_TriggerDuration_MustBeWithinZeroAndADay(int seconds, bool valid)
    {
        var rule = ValidRule();
        rule.Trigger = new RuleTrigger { Kind = TriggerKind.OnBatteryFor, DurationSeconds = seconds };

        Assert.Equal(valid, !ValidateRules(rule).Any(p => p.ErrorKey == "Validation_RuleDurationRange"));
    }

    [Theory]
    [InlineData(TriggerKind.FlagsActive)]
    [InlineData(TriggerKind.FlagsBecameActive)]
    [InlineData(TriggerKind.FlagsBecameInactive)]
    public void Validate_FlagTriggerWithoutFlags_ReportsProblem(TriggerKind kind)
    {
        var rule = ValidRule();
        rule.Trigger = new RuleTrigger { Kind = kind, Flags = UpsStatus.None };

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].Trigger.Flags", "Validation_RuleFlagsRequired");
    }

    [Theory]
    [InlineData(TriggerKind.ValueAtOrBelow, double.NaN)]
    [InlineData(TriggerKind.ValueAtOrAbove, double.PositiveInfinity)]
    [InlineData(TriggerKind.ValueAtOrBelow, double.NegativeInfinity)]
    public void Validate_ValueTriggerThreshold_MustBeFinite(TriggerKind kind, double threshold)
    {
        var rule = ValidRule();
        rule.Trigger = new RuleTrigger { Kind = kind, Metric = RuleMetric.LoadPercent, Threshold = threshold };

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].Trigger.Threshold", "Validation_RuleThresholdInvalid");
    }

    [Fact]
    public void Validate_ValueTriggerWithFiniteThreshold_AndConnectionTriggerWithoutFlags_AreFine()
    {
        var value = ValidRule();
        value.Trigger = new RuleTrigger { Kind = TriggerKind.ValueAtOrBelow, Metric = RuleMetric.BatteryCharge, Threshold = -5.5 };
        var lost = ValidRule();
        lost.Trigger = new RuleTrigger { Kind = TriggerKind.ConnectionLost, DurationSeconds = 30 };

        Assert.Empty(ValidateRules(value, lost));
    }

    [Theory]
    [InlineData(ActionKind.Notify)]
    [InlineData(ActionKind.Log)]
    public void Validate_NotifyAndLogWithoutMessage_ReportProblem(ActionKind kind)
    {
        var rule = ValidRule();
        rule.Actions = [new RuleAction { Kind = kind, Message = "  " }];

        AssertProblem(ValidateRules(rule), "Actions.Rules[0].Actions[0].Message", "Validation_RuleMessageRequired");
    }

    [Fact]
    public void Validate_RunCommand_NeedsACommandAndATimeoutOfOneToThirtySixHundredSeconds()
    {
        var rule = ValidRule();
        rule.Actions = [RuleAction.RunCommand(" "), RuleAction.RunCommand("tool", timeoutSeconds: 0), RuleAction.RunCommand("tool", timeoutSeconds: 3601),
            RuleAction.RunCommand("tool", timeoutSeconds: 1), RuleAction.RunCommand("tool", timeoutSeconds: 3600)];

        var problems = ValidateRules(rule);

        AssertProblem(problems, "Actions.Rules[0].Actions[0].Command", "Validation_RuleCommandRequired");
        AssertProblem(problems, "Actions.Rules[0].Actions[1].TimeoutSeconds", "Validation_RuleTimeoutRange");
        AssertProblem(problems, "Actions.Rules[0].Actions[2].TimeoutSeconds", "Validation_RuleTimeoutRange");
        Assert.DoesNotContain(problems, p => p.PropertyPath.StartsWith("Actions.Rules[0].Actions[3]", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.PropertyPath.StartsWith("Actions.Rules[0].Actions[4]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("not a url", false)]
    [InlineData("/relative/path", false)]
    [InlineData("ftp://example.org/hook", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("http://localhost:8080/hook", true)]
    [InlineData("https://example.org/hook?token=x", true)]
    public void Validate_WebhookUrl_MustBeAbsoluteHttpOrHttps(string url, bool valid)
    {
        var rule = ValidRule();
        rule.Actions = [RuleAction.Webhook(url)];

        Assert.Equal(valid, !ValidateRules(rule).Any(p => p.ErrorKey == "Validation_RuleUrlInvalid"));
    }

    [Fact]
    public void Validate_StopComputerAction_NeedsNothingElse()
    {
        var rule = ValidRule();
        rule.Actions = [RuleAction.StopComputer(CocoNut.Core.Abstractions.StopAction.Hibernate)];

        Assert.Empty(ValidateRules(rule));
    }

    [Fact]
    public void Validate_NullSectionsFromAHandEditedFile_DoNotThrow()
    {
        var rule = ValidRule();
        rule.Actions = null!;
        var other = ValidRule();
        other.Trigger = null!;

        var problems = ValidateRules(rule, other);

        AssertProblem(problems, "Actions.Rules[0].Actions", "Validation_RuleActionsRequired");
        Assert.Contains(problems, p => p.PropertyPath.StartsWith("Actions.Rules[1].Trigger", StringComparison.Ordinal));
        Assert.Empty(AppSettingsValidator.Validate(new AppSettings { Actions = new ActionsSettings { Rules = null! } }));
    }
}
