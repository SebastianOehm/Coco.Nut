using System.Globalization;
using CocoNut.Core.History;

namespace CocoNut.Core.Tests.History;

public class HistoryCsvExporterTests
{
    private static readonly UpsKey Ups = new("nut.example", 3493, "ups");

    /// <summary>Runs <paramref name="action"/> under a culture that formats numbers with a decimal comma.</summary>
    private static async Task<string> RunUnderGermanCultureAsync(Func<StringWriter, Task> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var writer = new StringWriter(CultureInfo.CurrentCulture);
            await action(writer);
            return writer.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Samples_WriteHeaderAndInvariantRows_UnderAGermanCulture()
    {
        var sample = new HistorySample(
            new DateTimeOffset(2026, 3, 1, 14, 30, 5, 120, TimeSpan.FromHours(2)),
            "OB DISCHRG", 55.5, 12.25, 900, 231.2, 50, null, 37.5, 410.75, true);

        var csv = await RunUnderGermanCultureAsync(w => HistoryCsvExporter.WriteSamplesAsync(w, [sample]));

        var lines = csv.Split("\r\n");
        Assert.Equal(
            "timestamp_utc,status,battery_charge_percent,battery_voltage_v,battery_runtime_s,input_voltage_v," +
            "input_frequency_hz,output_voltage_v,load_percent,output_power_w,aggregated",
            lines[0]);
        Assert.Equal("2026-03-01T12:30:05.120Z,OB DISCHRG,55.5,12.25,900,231.2,50,,37.5,410.75,1", lines[1]);
        Assert.Equal(string.Empty, lines[2]); // the last row ends with CRLF
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public async Task Samples_WithNoRows_WriteOnlyTheHeader()
    {
        var writer = new StringWriter();

        await HistoryCsvExporter.WriteSamplesAsync(writer, []);

        Assert.Single(writer.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Events_WriteHeaderAndRows_WithQuotingPerRfc4180()
    {
        var at = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            new HistoryEvent(at, Ups, HistoryEventKind.OnBattery, "status=OB, charge=90%"),
            new HistoryEvent(at.AddSeconds(1), Ups, HistoryEventKind.ConnectionLost, "said \"no\""),
            new HistoryEvent(at.AddSeconds(2), Ups, HistoryEventKind.ShutdownFailed, "line1\nline2"),
            new HistoryEvent(at.AddSeconds(3), new UpsKey("h,ost", 1, "u\"ps"), HistoryEventKind.Connected),
        };

        var csv = await RunUnderGermanCultureAsync(w => HistoryCsvExporter.WriteEventsAsync(w, events));

        Assert.Equal(
            "timestamp_utc,host,port,ups,kind,detail\r\n" +
            "2026-03-01T12:00:00.000Z,nut.example,3493,ups,OnBattery,\"status=OB, charge=90%\"\r\n" +
            "2026-03-01T12:00:01.000Z,nut.example,3493,ups,ConnectionLost,\"said \"\"no\"\"\"\r\n" +
            "2026-03-01T12:00:02.000Z,nut.example,3493,ups,ShutdownFailed,\"line1\nline2\"\r\n" +
            "2026-03-01T12:00:03.000Z,\"h,ost\",1,\"u\"\"ps\",Connected,\r\n",
            csv);
    }

    [Fact]
    public async Task Export_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sample = new HistorySample(DateTimeOffset.UnixEpoch, "OL");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => HistoryCsvExporter.WriteSamplesAsync(new StringWriter(), [sample], cts.Token));
    }
}
