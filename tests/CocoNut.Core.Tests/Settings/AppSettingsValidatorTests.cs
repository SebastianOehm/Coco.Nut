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
}
