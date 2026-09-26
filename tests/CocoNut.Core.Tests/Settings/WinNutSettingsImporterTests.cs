using CocoNut.Core.Abstractions;
using CocoNut.Core.Settings;
using CocoNut.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Tests.Settings;

public sealed class WinNutSettingsImporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>
    /// A representative WinNUT 2.x <c>user.config</c>, based on the setting names in WinNUT's
    /// <c>My Project/Settings.settings</c>. <c>NUT_Username</c>/<c>NUT_Password</c> use the XML serialization
    /// form <c>System.Configuration</c> falls back to for a type with no <see cref="System.ComponentModel.TypeConverter"/>
    /// (WinNUT's <c>SerializedProtectedString</c> has none); the placeholder text is not real DPAPI output, which
    /// is fine since decryption is only ever attempted on Windows.
    /// </summary>
    private const string FixtureXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <configSections>
            <sectionGroup name="userSettings" type="System.Configuration.UserSettingsGroup, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089">
              <section name="WinNUT_Client.My.MySettings" type="System.Configuration.ClientSettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089" allowExeDefinition="MachineToLocalUser" requirePermission="false" />
            </sectionGroup>
          </configSections>
          <userSettings>
            <WinNUT_Client.My.MySettings>
              <setting name="StartWithWindows" serializeAs="String"><value>True</value></setting>
              <setting name="CloseToTray" serializeAs="String"><value>True</value></setting>
              <setting name="MinimizeOnStart" serializeAs="String"><value>False</value></setting>
              <setting name="MinimizeToTray" serializeAs="String"><value>True</value></setting>
              <setting name="LG_LogToFile" serializeAs="String"><value>True</value></setting>
              <setting name="LG_LogLevel" serializeAs="String"><value>3</value></setting>
              <setting name="UP_CheckAtStart" serializeAs="String"><value>True</value></setting>
              <setting name="UP_AutoChkDelay" serializeAs="String"><value>1</value></setting>
              <setting name="UP_Branch" serializeAs="String"><value>1</value></setting>
              <setting name="UP_LastCheck" serializeAs="String"><value>2024-03-15T10:30:00Z</value></setting>
              <setting name="PW_BattChrgFloor" serializeAs="String"><value>25</value></setting>
              <setting name="PW_RuntimeFloor" serializeAs="String"><value>180</value></setting>
              <setting name="PW_Immediate" serializeAs="String"><value>True</value></setting>
              <setting name="PW_RespectFSD" serializeAs="String"><value>True</value></setting>
              <setting name="PW_StopType" serializeAs="String"><value>2</value></setting>
              <setting name="PW_StopDelaySec" serializeAs="String"><value>20</value></setting>
              <setting name="PW_UserExtendStopTimer" serializeAs="String"><value>True</value></setting>
              <setting name="PW_ExtendDelaySec" serializeAs="String"><value>25</value></setting>
              <setting name="NUT_ServerAddress" serializeAs="String"><value>192.168.1.50</value></setting>
              <setting name="NUT_ServerPort" serializeAs="String"><value>3495</value></setting>
              <setting name="NUT_UPSName" serializeAs="String"><value>office-ups</value></setting>
              <setting name="NUT_PollIntervalMsec" serializeAs="String"><value>1500</value></setting>
              <setting name="NUT_Username" serializeAs="Xml">
                <value>
                  <SerializedProtectedString xmlns="http://schemas.datacontract.org/2004/07/WinNUT_Client_Common">
                    <ProtectedValue>ZmFrZS1wcm90ZWN0ZWQtdXNlcm5hbWU=</ProtectedValue>
                  </SerializedProtectedString>
                </value>
              </setting>
              <setting name="NUT_Password" serializeAs="Xml">
                <value>
                  <SerializedProtectedString xmlns="http://schemas.datacontract.org/2004/07/WinNUT_Client_Common">
                    <ProtectedValue>ZmFrZS1wcm90ZWN0ZWQtcGFzc3dvcmQ=</ProtectedValue>
                  </SerializedProtectedString>
                </value>
              </setting>
              <setting name="NUT_AutoReconnect" serializeAs="String"><value>True</value></setting>
              <setting name="CAL_VoltInMin" serializeAs="String"><value>200</value></setting>
              <setting name="CAL_VoltInMax" serializeAs="String"><value>260</value></setting>
              <setting name="CAL_FreqInNom" serializeAs="String"><value>60</value></setting>
              <setting name="CAL_FreqInMin" serializeAs="String"><value>55</value></setting>
              <setting name="CAL_FreqInMax" serializeAs="String"><value>65</value></setting>
              <setting name="CAL_VoltOutMin" serializeAs="String"><value>205</value></setting>
              <setting name="CAL_VoltOutMax" serializeAs="String"><value>255</value></setting>
              <setting name="CAL_LoadMin" serializeAs="String"><value>5</value></setting>
              <setting name="CAL_LoadMax" serializeAs="String"><value>95</value></setting>
              <setting name="CAL_BattVMin" serializeAs="String"><value>10</value></setting>
              <setting name="CAL_BattVMax" serializeAs="String"><value>14</value></setting>
              <setting name="IsFirstRun" serializeAs="String"><value>False</value></setting>
            </WinNUT_Client.My.MySettings>
          </userSettings>
        </configuration>
        """;

    private string WriteFixture(string xml = FixtureXml)
    {
        var path = Path.Combine(_temp.Path, "user.config");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void Import_MapsGeneralAndLoggingSettings()
    {
        var settings = WinNutSettingsImporter.Import(WriteFixture());

        Assert.True(settings.General.StartWithOs);
        Assert.True(settings.General.CloseToTray);
        Assert.False(settings.General.MinimizeOnStart);
        Assert.True(settings.General.MinimizeToTray);
        Assert.False(settings.General.IsFirstRun);

        Assert.True(settings.Logging.LogToFile);
        Assert.Equal(LogLevel.Debug, settings.Logging.MinimumLevel); // LG_LogLevel 3 -> Debug
    }

    [Theory]
    [InlineData(0, LogLevel.Information)]
    [InlineData(1, LogLevel.Warning)]
    [InlineData(2, LogLevel.Error)]
    [InlineData(3, LogLevel.Debug)]
    public void Import_MapsEveryLogLevelIndex(int index, LogLevel expected)
    {
        var xml = FixtureXml.Replace(
            "<setting name=\"LG_LogLevel\" serializeAs=\"String\"><value>3</value></setting>",
            $"<setting name=\"LG_LogLevel\" serializeAs=\"String\"><value>{index}</value></setting>");

        var settings = WinNutSettingsImporter.Import(WriteFixture(xml));

        Assert.Equal(expected, settings.Logging.MinimumLevel);
    }

    [Fact]
    public void Import_MapsUpdateSettings()
    {
        var settings = WinNutSettingsImporter.Import(WriteFixture());

        Assert.True(settings.Update.CheckAtStart);
        Assert.Equal(7, settings.Update.AutoCheckIntervalDays); // UP_AutoChkDelay 1 (Weekly) -> 7 days
        Assert.Equal(UpdateChannel.PreRelease, settings.Update.Channel); // UP_Branch 1
        Assert.Equal(new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero), settings.Update.LastCheck);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 7)]
    [InlineData(2, 30)]
    public void Import_MapsEveryAutoCheckDelayIndex(int index, int expectedDays)
    {
        var xml = FixtureXml.Replace(
            "<setting name=\"UP_AutoChkDelay\" serializeAs=\"String\"><value>1</value></setting>",
            $"<setting name=\"UP_AutoChkDelay\" serializeAs=\"String\"><value>{index}</value></setting>");

        var settings = WinNutSettingsImporter.Import(WriteFixture(xml));

        Assert.Equal(expectedDays, settings.Update.AutoCheckIntervalDays);
    }

    [Fact]
    public void Import_MapsPowerSettings()
    {
        var settings = WinNutSettingsImporter.Import(WriteFixture());

        Assert.Equal(25, settings.Power.BatteryChargeFloor);
        Assert.Equal(180, settings.Power.RuntimeFloorSeconds);
        Assert.True(settings.Power.StopImmediately);
        Assert.True(settings.Power.RespectFsd);
        Assert.Equal(StopAction.Hibernate, settings.Power.StopAction); // PW_StopType 2
        Assert.Equal(20, settings.Power.StopDelaySeconds);
        Assert.True(settings.Power.AllowExtendDelay);
        Assert.Equal(25, settings.Power.ExtendDelaySeconds);
    }

    [Theory]
    [InlineData(0, StopAction.Shutdown)]
    [InlineData(1, StopAction.Suspend)]
    [InlineData(2, StopAction.Hibernate)]
    public void Import_MapsEveryStopTypeIndex(int index, StopAction expected)
    {
        var xml = FixtureXml.Replace(
            "<setting name=\"PW_StopType\" serializeAs=\"String\"><value>2</value></setting>",
            $"<setting name=\"PW_StopType\" serializeAs=\"String\"><value>{index}</value></setting>");

        var settings = WinNutSettingsImporter.Import(WriteFixture(xml));

        Assert.Equal(expected, settings.Power.StopAction);
    }

    [Fact]
    public void Import_MapsConnectionSettings_ButLeavesSecretsNullOnNonWindows()
    {
        var settings = WinNutSettingsImporter.Import(WriteFixture());

        Assert.Equal("192.168.1.50", settings.Connection.Host);
        Assert.Equal(3495, settings.Connection.Port);
        Assert.Equal("office-ups", settings.Connection.UpsName);
        Assert.Equal(1500, settings.Connection.PollIntervalMs);
        Assert.True(settings.Connection.AutoReconnect);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Null(settings.Connection.Username);
            Assert.Null(settings.Connection.Password);
        }
    }

    [Fact]
    public void Import_MapsCalibrationSettings()
    {
        var settings = WinNutSettingsImporter.Import(WriteFixture());

        Assert.Equal(200, settings.Calibration.InputVoltageMin);
        Assert.Equal(260, settings.Calibration.InputVoltageMax);
        Assert.Equal(60, settings.Calibration.InputFrequencyNominal);
        Assert.Equal(55, settings.Calibration.InputFrequencyMin);
        Assert.Equal(65, settings.Calibration.InputFrequencyMax);
        Assert.Equal(205, settings.Calibration.OutputVoltageMin);
        Assert.Equal(255, settings.Calibration.OutputVoltageMax);
        Assert.Equal(5, settings.Calibration.LoadMin);
        Assert.Equal(95, settings.Calibration.LoadMax);
        Assert.Equal(10, settings.Calibration.BatteryVoltageMin);
        Assert.Equal(14, settings.Calibration.BatteryVoltageMax);
    }

    [Fact]
    public void Import_WithMissingSettings_KeepsBaselineValues()
    {
        const string minimalXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <userSettings>
                <WinNUT_Client.My.MySettings>
                  <setting name="NUT_ServerAddress" serializeAs="String"><value>only-this-is-set</value></setting>
                </WinNUT_Client.My.MySettings>
              </userSettings>
            </configuration>
            """;
        var baseline = new AppSettings();
        baseline.Connection.Port = 9999;
        baseline.Power.BatteryChargeFloor = 55;

        var settings = WinNutSettingsImporter.Import(WriteFixture(minimalXml), baseline);

        Assert.Equal("only-this-is-set", settings.Connection.Host);
        Assert.Equal(9999, settings.Connection.Port); // Not present in the file: baseline value kept.
        Assert.Equal(55, settings.Power.BatteryChargeFloor); // Not present in the file: baseline value kept.
    }

    [Fact]
    public void Import_DoesNotMutateTheBaselineInstance()
    {
        var baseline = new AppSettings();
        var originalHost = baseline.Connection.Host;

        WinNutSettingsImporter.Import(WriteFixture(), baseline);

        Assert.Equal(originalHost, baseline.Connection.Host);
    }

    [Fact]
    public void Import_WithoutRecognizableSection_ReturnsBaselineUnchanged()
    {
        const string unrelatedXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <userSettings>
                <SomeOtherApp.Settings>
                  <setting name="Whatever" serializeAs="String"><value>1</value></setting>
                </SomeOtherApp.Settings>
              </userSettings>
            </configuration>
            """;

        var settings = WinNutSettingsImporter.Import(WriteFixture(unrelatedXml));

        Assert.Equal(new AppSettings().Connection.Host, settings.Connection.Host);
    }

    [Fact]
    public void FindWinNutConfigFiles_OnNonWindows_ReturnsEmpty()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Exercising the real %LOCALAPPDATA% search would touch the real user profile.
        }

        Assert.Empty(WinNutSettingsImporter.FindWinNutConfigFiles());
    }
}
