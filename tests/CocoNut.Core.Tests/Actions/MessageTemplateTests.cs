using System.Globalization;
using CocoNut.Core.Actions;
using CocoNut.Core.Ups;

namespace CocoNut.Core.Tests.Actions;

public class MessageTemplateTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly UpsReading FullReading = new()
    {
        Timestamp = Now,
        Status = UpsStatus.OB,
        RawStatus = "OB DISCHRG",
        BatteryCharge = 12.5,
        BatteryRuntime = TimeSpan.FromSeconds(300),
        LoadPercent = 40,
        InputVoltage = 0,
        OutputVoltage = 229.5,
        OutputPowerWatts = 1234.567,
        BatteryVoltage = 24.1,
    };

    private static ActionContext Context(UpsReading? reading) =>
        new(new ActionRule { Name = "Low battery" }, "myups", reading, Now);

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void Expand_ReplacesEveryPlaceholder()
    {
        var text = MessageTemplate.Expand(
            "{rule}|{ups}|{status}|{charge}|{runtime}|{load}|{inputVoltage}|{outputVoltage}|{outputPower}|{batteryVoltage}",
            Context(FullReading),
            CultureInfo.InvariantCulture);

        Assert.Equal("Low battery|myups|OB DISCHRG|12.5|300|40|0|229.5|1234.57|24.1", text);
    }

    [Fact]
    public void Expand_FormatsNumbersWithTheGivenCulture()
    {
        var text = MessageTemplate.Expand("{charge}% / {outputPower} W", Context(FullReading), German);

        Assert.Equal("12,5% / 1234,57 W", text);
    }

    [Fact]
    public void Expand_Time_IsIso8601WithOffset_RegardlessOfCulture()
    {
        var expected = Now.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        Assert.Equal(expected, MessageTemplate.Expand("{time}", Context(FullReading), German));
        Assert.Equal(expected, MessageTemplate.Expand("{time}", Context(FullReading), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Expand_UnknownPlaceholdersAndStrayBraces_AreLeftAsTheyAre()
    {
        var text = MessageTemplate.Expand("{nope} {Rule} {} { rule } {{rule}} {rule", Context(FullReading), CultureInfo.InvariantCulture);

        // Names are case sensitive; "{{rule}}" keeps its outer braces around the replaced name.
        Assert.Equal("{nope} {Rule} {} { rule } {Low battery} {rule", text);
    }

    [Fact]
    public void Expand_UnknownValues_BecomeEmptyStrings()
    {
        var partial = new UpsReading { Timestamp = Now, Status = UpsStatus.OL, RawStatus = "OL" };

        Assert.Equal("[OL][][][]", MessageTemplate.Expand("[{status}][{charge}][{runtime}][{outputPower}]", Context(partial), CultureInfo.InvariantCulture));
        Assert.Equal("[][][]", MessageTemplate.Expand("[{status}][{charge}][{load}]", Context(null), CultureInfo.InvariantCulture));
        Assert.Equal("Low battery on myups", MessageTemplate.Expand("{rule} on {ups}", Context(null), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Expand_EmptyTemplate_IsEmpty() =>
        Assert.Equal(string.Empty, MessageTemplate.Expand(string.Empty, Context(FullReading), CultureInfo.InvariantCulture));

    [Fact]
    public void GetEnvironment_UsesTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = German;
        try
        {
            var environment = Context(FullReading).GetEnvironment();

            Assert.Equal("Low battery", environment["COCONUT_RULE"]);
            Assert.Equal("myups", environment["COCONUT_UPS"]);
            Assert.Equal("OB DISCHRG", environment["COCONUT_STATUS"]);
            Assert.Equal("12.5", environment["COCONUT_BATTERY_CHARGE"]);
            Assert.Equal("300", environment["COCONUT_BATTERY_RUNTIME"]);
            Assert.Equal("40", environment["COCONUT_LOAD"]);
            Assert.Equal("0", environment["COCONUT_INPUT_VOLTAGE"]);
            Assert.Equal("229.5", environment["COCONUT_OUTPUT_VOLTAGE"]);
            Assert.Equal("1234.57", environment["COCONUT_OUTPUT_POWER"]);
            Assert.Equal("24.1", environment["COCONUT_BATTERY_VOLTAGE"]);
            Assert.Equal(10, environment.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void GetEnvironment_WithoutReading_HasEmptyValuesForEveryUpsVariable()
    {
        var environment = Context(null).GetEnvironment();

        Assert.Equal("Low battery", environment["COCONUT_RULE"]);
        Assert.Equal("myups", environment["COCONUT_UPS"]);
        foreach (var name in new[] { "COCONUT_STATUS", "COCONUT_BATTERY_CHARGE", "COCONUT_BATTERY_RUNTIME", "COCONUT_LOAD",
                     "COCONUT_INPUT_VOLTAGE", "COCONUT_OUTPUT_VOLTAGE", "COCONUT_OUTPUT_POWER", "COCONUT_BATTERY_VOLTAGE" })
        {
            Assert.Equal(string.Empty, environment[name]);
        }
    }

    [Fact]
    public void WebhookPayload_SerializesCamelCaseWithNullsForUnknownValues()
    {
        var partial = new UpsReading { Timestamp = Now, Status = UpsStatus.OL, RawStatus = "OL", BatteryCharge = 80 };

        var json = System.Text.Json.JsonSerializer.Serialize(WebhookPayload.From(Context(partial)), WebhookJsonContext.Default.WebhookPayload);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("Low battery", root.GetProperty("rule").GetString());
        Assert.Equal("myups", root.GetProperty("ups").GetString());
        Assert.Equal("OL", root.GetProperty("status").GetString());
        Assert.Equal(Now, root.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(80, root.GetProperty("batteryCharge").GetDouble());
        foreach (var name in new[] { "batteryRuntime", "load", "inputVoltage", "outputVoltage", "outputPower", "batteryVoltage" })
        {
            Assert.Equal(System.Text.Json.JsonValueKind.Null, root.GetProperty(name).ValueKind);
        }
    }

    [Fact]
    public void WebhookPayload_WithoutReading_HasNullStatus()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(WebhookPayload.From(Context(null)), WebhookJsonContext.Default.WebhookPayload);

        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, document.RootElement.GetProperty("status").ValueKind);
    }
}
