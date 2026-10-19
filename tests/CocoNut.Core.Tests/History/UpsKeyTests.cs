using CocoNut.Core.History;
using CocoNut.Core.Settings;

namespace CocoNut.Core.Tests.History;

public class UpsKeyTests
{
    [Fact]
    public void Equality_IgnoresHostCase_ButNotUpsNameCase()
    {
        var key = new UpsKey("Nut.Example", 3493, "ups");

        Assert.Equal(key, new UpsKey("nut.example", 3493, "ups"));
        Assert.Equal(key.GetHashCode(), new UpsKey("NUT.EXAMPLE", 3493, "ups").GetHashCode());
        Assert.NotEqual(key, new UpsKey("nut.example", 3493, "UPS"));
        Assert.NotEqual(key, new UpsKey("nut.example", 3494, "ups"));
    }

    [Fact]
    public void From_ConnectionSettings_CopiesHostPortAndName()
    {
        var key = UpsKey.From(new ConnectionSettings { Host = "h", Port = 1234, UpsName = "u" });

        Assert.Equal(new UpsKey("h", 1234, "u"), key);
    }

    [Fact]
    public void HistorySample_FromReading_MapsEveryValue()
    {
        var reading = new Core.Ups.UpsReading
        {
            Timestamp = DateTimeOffset.UnixEpoch,
            Status = Core.Ups.UpsStatus.OB,
            RawStatus = "OB",
            BatteryCharge = 50,
            BatteryVoltage = 12,
            BatteryRuntime = TimeSpan.FromMinutes(5),
            InputVoltage = 230,
            InputFrequency = 50,
            OutputVoltage = 229,
            LoadPercent = 30,
            OutputPowerWatts = 120,
        };

        var sample = HistorySample.FromReading(reading);

        Assert.Equal(new HistorySample(DateTimeOffset.UnixEpoch, "OB", 50, 12, 300, 230, 50, 229, 30, 120, false), sample);
    }
}
