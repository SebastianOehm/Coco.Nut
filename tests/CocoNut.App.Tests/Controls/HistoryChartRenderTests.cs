using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using CocoNut.App.Controls;
using CocoNut.App.Services;
using CocoNut.App.Tests.Fakes;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.History;
using CocoNut.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace CocoNut.App.Tests.Controls;

/// <summary>Headless tests that <see cref="HistoryChart"/> and the history window measure and render whatever data they are given.</summary>
public class HistoryChartRenderTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    private static HistoryChart CreateChart(IReadOnlyList<HistoryChartPoint>? points) => new()
    {
        Width = 600,
        Caption = "Battery charge",
        Unit = "%",
        Minimum = 0,
        Maximum = 100,
        NoDataText = "No data",
        RangeStart = Start,
        RangeEnd = Start.AddHours(24),
        Points = points,
    };

    private static WriteableBitmap? Render(HistoryChart chart)
    {
        var window = new Window { Content = chart, Width = 640, Height = 260 };
        window.Show();
        return window.CaptureRenderedFrame();
    }

    private static List<HistoryChartPoint> Sine(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new HistoryChartPoint(Start.AddMinutes(i * 10), 60 + 30 * Math.Sin(i / 8d)))
            .ToList();

    [AvaloniaFact]
    public void Chart_with_data_measures_to_its_chart_height_and_renders()
    {
        var chart = CreateChart(Sine(140));

        var frame = Render(chart);

        Assert.NotNull(frame);
        Assert.True(chart.DesiredSize.Height > 100);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public void Chart_without_enough_points_renders_without_throwing(int count)
    {
        var chart = CreateChart(Sine(count));

        Assert.NotNull(Render(chart));
    }

    [AvaloniaFact]
    public void Chart_with_null_points_and_no_data_collapses_to_a_caption_and_renders()
    {
        var empty = CreateChart([]);
        var allNull = CreateChart(Enumerable.Range(0, 20).Select(i => new HistoryChartPoint(Start.AddMinutes(i), null)).ToList());
        var populated = CreateChart(Sine(50));

        Assert.NotNull(Render(empty));
        Assert.NotNull(Render(allNull));
        Assert.NotNull(Render(populated));

        Assert.True(allNull.DesiredSize.Height < populated.DesiredSize.Height);
        Assert.Equal(empty.DesiredSize.Height, allNull.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void Chart_without_a_range_or_points_renders_without_throwing()
    {
        var chart = new HistoryChart { Width = 300, Height = 150 };

        Assert.NotNull(Render(chart));
    }

    [AvaloniaFact]
    public void Chart_with_a_gap_and_null_values_renders()
    {
        var points = Sine(60)
            .Select((p, i) => i is >= 20 and < 25 ? p with { Value = null } : p)
            .Where((_, i) => i is < 35 or >= 50)
            .ToList();

        Assert.NotNull(Render(CreateChart(points)));
    }

    [AvaloniaFact]
    public void Chart_in_a_tiny_space_does_not_throw()
    {
        var chart = CreateChart(Sine(20));
        chart.Width = 40;
        chart.Height = 30;

        Assert.NotNull(Render(chart));
    }

    [AvaloniaFact]
    public void Hovering_snaps_to_the_nearest_point_and_leaving_clears_it()
    {
        var chart = CreateChart(Sine(140));
        var window = new Window { Content = chart, Width = 640, Height = 260 };
        window.Show();
        window.CaptureRenderedFrame();
        Assert.Equal(-1, chart.HoveredIndex);

        window.MouseMove(new Point(320, 100), RawInputModifiers.None);
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.InRange(chart.HoveredIndex, 1, 138);

        window.MouseMove(new Point(630, 250), RawInputModifiers.None);
        window.MouseMove(new Point(-5, -5), RawInputModifiers.None);
        window.CaptureRenderedFrame();
    }

    [AvaloniaFact]
    public void History_window_renders_with_data_events_and_an_all_null_metric()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var settings = new AppSettings();
        var ups = UpsKey.From(settings.Connection);
        var store = new FakeHistoryStore { KnownUps = { ups } };
        for (int i = 0; i < 100; i++)
        {
            store.Samples.Add(new HistorySample(now.AddMinutes(-i * 10), "OL", BatteryCharge: 100 - i % 7, LoadPercent: 20 + i % 5, InputVoltage: 230 + i % 3));
        }

        store.Events.Add(new HistoryEvent(now.AddHours(-3), ups, HistoryEventKind.OnBattery, "charge=98%"));
        store.Events.Add(new HistoryEvent(now.AddHours(-2), ups, HistoryEventKind.OnLine));

        var settingsStore = new InMemorySettingsStore { Settings = settings };
        var viewModel = new HistoryViewModel(
            store, new SettingsService(settingsStore, settings), new FakeFileSaveDialog(), new FakeNotificationService(),
            new ImmediateUiDispatcher(), new FakeTimeProvider(now), NullLogger<HistoryViewModel>.Instance);

        var window = new HistoryWindow { DataContext = viewModel };
        window.Show();
        var frame = window.CaptureRenderedFrame();

        Assert.NotNull(frame);
        Assert.Equal(5, viewModel.Charts.Count);
        Assert.False(viewModel.Charts[1].HasData); // runtime was never reported
        Assert.Equal(2, viewModel.Events.Count);

        window.Close();
    }
}
