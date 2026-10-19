using CocoNut.Core.History;
using CocoNut.Core.Tests.TestSupport;
using Microsoft.Data.Sqlite;

namespace CocoNut.Core.Tests.History;

public sealed class SqliteHistoryStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly UpsKey Ups1 = new("nut.example", 3493, "ups");
    private static readonly UpsKey Ups2 = new("other.example", 3493, "rack");

    private readonly TempDirectory _temp = new();
    private readonly List<SqliteHistoryStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        _temp.Dispose();
    }

    private string DbPath => Path.Combine(_temp.Path, "history.db");

    private SqliteHistoryStore CreateStore()
    {
        var store = new SqliteHistoryStore(DbPath);
        _stores.Add(store);
        return store;
    }

    private static HistorySample Sample(DateTimeOffset at, double? charge = 80, string status = "OL", double? load = 40) =>
        new(at, status, BatteryCharge: charge, LoadPercent: load);

    [Fact]
    public async Task Samples_RoundTrip_IncludingNullsAndAllFields()
    {
        var store = CreateStore();
        var full = new HistorySample(T0, "OB DISCHRG", 55.5, 12.3, 900, 231.2, 50.1, 229.9, 37.5, 410, false);
        var sparse = new HistorySample(T0.AddSeconds(10), "OL");

        await store.AppendSamplesAsync(Ups1, [full, sparse]);
        var loaded = await store.GetSamplesAsync(Ups1, T0.AddHours(-1), T0.AddHours(1), null);

        Assert.Equal([full, sparse], loaded);
        Assert.All(loaded, s => Assert.Equal(TimeSpan.Zero, s.Timestamp.Offset));
    }

    [Fact]
    public async Task Samples_AreFilteredByTimeRange_AndOrderedByTime()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1,
        [
            Sample(T0.AddMinutes(30)), Sample(T0), Sample(T0.AddMinutes(10)), Sample(T0.AddMinutes(20)),
        ]);

        var loaded = await store.GetSamplesAsync(Ups1, T0.AddMinutes(5), T0.AddMinutes(20), null);

        Assert.Equal([T0.AddMinutes(10), T0.AddMinutes(20)], loaded.Select(s => s.Timestamp));
    }

    [Fact]
    public async Task Samples_WithMaxPoints_AreAveragedIntoBuckets()
    {
        var store = CreateStore();
        // 100 samples, one per second; charge climbs 0..99, status flips to OB for the last ten.
        var samples = Enumerable.Range(0, 100)
            .Select(i => Sample(T0.AddSeconds(i), charge: i, status: i >= 90 ? "OB" : "OL"))
            .ToList();
        await store.AppendSamplesAsync(Ups1, samples);

        var loaded = await store.GetSamplesAsync(Ups1, T0, T0.AddSeconds(99), maxPoints: 10);

        Assert.InRange(loaded.Count, 9, 10);
        Assert.All(loaded, s => Assert.True(s.IsAggregated));
        Assert.Equal(loaded.OrderBy(s => s.Timestamp), loaded);
        // Averages of a monotonic ramp stay monotonic and bracket the data.
        Assert.Equal(loaded.Select(s => s.BatteryCharge).OrderBy(c => c), loaded.Select(s => s.BatteryCharge));
        Assert.InRange(loaded[0].BatteryCharge!.Value, 0, 15);
        Assert.InRange(loaded[^1].BatteryCharge!.Value, 85, 99);
        Assert.Equal("OL", loaded[0].Status);
        Assert.Equal("OB", loaded[^1].Status); // last status of the bucket
    }

    [Fact]
    public async Task Samples_WithMaxPoints_AverageOnlyReportedValues()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1,
        [
            Sample(T0, charge: 10, load: null),
            Sample(T0.AddSeconds(1), charge: 20, load: 50),
            Sample(T0.AddSeconds(2), charge: null, load: null),
            Sample(T0.AddSeconds(3), charge: null, load: null),
        ]);

        var loaded = await store.GetSamplesAsync(Ups1, T0, T0.AddSeconds(3), maxPoints: 2);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(15, loaded[0].BatteryCharge);
        Assert.Equal(50, loaded[0].LoadPercent);
        Assert.Null(loaded[1].BatteryCharge);
        Assert.Null(loaded[1].LoadPercent);
    }

    [Fact]
    public async Task Samples_WithMaxPoints_ReturnRawRowsWhenTheyFit()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1, [Sample(T0), Sample(T0.AddSeconds(5))]);

        var loaded = await store.GetSamplesAsync(Ups1, T0, T0.AddMinutes(1), maxPoints: 2);

        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, s => Assert.False(s.IsAggregated));
    }

    [Fact]
    public async Task Samples_OfUnknownUps_AreEmpty()
    {
        var store = CreateStore();

        Assert.Empty(await store.GetSamplesAsync(Ups1, T0, T0.AddHours(1), null));
    }

    [Fact]
    public async Task Ups_AreIsolatedFromEachOther()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1, [Sample(T0, charge: 11)]);
        await store.AppendSamplesAsync(Ups2, [Sample(T0, charge: 22)]);
        await store.AppendEventAsync(new HistoryEvent(T0, Ups1, HistoryEventKind.OnBattery));
        await store.AppendEventAsync(new HistoryEvent(T0, Ups2, HistoryEventKind.OnLine));

        var samples1 = await store.GetSamplesAsync(Ups1, T0, T0, null);
        var samples2 = await store.GetSamplesAsync(Ups2, T0, T0, null);
        var events1 = await store.GetEventsAsync(Ups1, T0, T0, null, 10);
        var allEvents = await store.GetEventsAsync(null, T0, T0, null, 10);

        Assert.Equal(11, Assert.Single(samples1).BatteryCharge);
        Assert.Equal(22, Assert.Single(samples2).BatteryCharge);
        Assert.Equal(HistoryEventKind.OnBattery, Assert.Single(events1).Kind);
        Assert.Equal(2, allEvents.Count);
        Assert.Equal([Ups1, Ups2], await store.GetKnownUpsAsync());
    }

    [Fact]
    public async Task Ups_HostIsMatchedCaseInsensitively()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1, [Sample(T0)]);

        var loaded = await store.GetSamplesAsync(new UpsKey("NUT.Example", 3493, "ups"), T0, T0, null);

        Assert.Single(loaded);
        Assert.Single(await store.GetKnownUpsAsync());
    }

    [Fact]
    public async Task Events_AreReturnedNewestFirst_WithinRange_AndLimited()
    {
        var store = CreateStore();
        for (var i = 0; i < 5; i++)
        {
            await store.AppendEventAsync(new HistoryEvent(T0.AddMinutes(i), Ups1, HistoryEventKind.OnBattery, $"n{i}"));
        }

        var newest = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), null, limit: 3);
        var ranged = await store.GetEventsAsync(Ups1, T0.AddMinutes(1), T0.AddMinutes(2), null, limit: 10);

        Assert.Equal(["n4", "n3", "n2"], newest.Select(e => e.Detail));
        Assert.Equal(["n2", "n1"], ranged.Select(e => e.Detail));
        Assert.Equal(Ups1, newest[0].Ups);
    }

    [Fact]
    public async Task Events_Search_MatchesKindNameOrDetail_CaseInsensitively()
    {
        var store = CreateStore();
        await store.AppendEventAsync(new HistoryEvent(T0, Ups1, HistoryEventKind.OnBattery, "charge=90%"));
        await store.AppendEventAsync(new HistoryEvent(T0.AddMinutes(1), Ups1, HistoryEventKind.ConnectionLost, "IOException: Connection reset"));
        await store.AppendEventAsync(new HistoryEvent(T0.AddMinutes(2), Ups1, HistoryEventKind.ShutdownFailed, null));

        var byKind = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "onbatt", 10);
        var byDetail = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "RESET", 10);
        var byKindPart = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "shutdown", 10);
        var literalPercent = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "90%", 10);
        var wildcardIsLiteral = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "%", 10);
        var none = await store.GetEventsAsync(Ups1, T0, T0.AddHours(1), "nothing like this", 10);

        Assert.Equal(HistoryEventKind.OnBattery, Assert.Single(byKind).Kind);
        Assert.Equal(HistoryEventKind.ConnectionLost, Assert.Single(byDetail).Kind);
        Assert.Equal(HistoryEventKind.ShutdownFailed, Assert.Single(byKindPart).Kind);
        Assert.Single(literalPercent);
        Assert.Single(wildcardIsLiteral);
        Assert.Empty(none);
    }

    [Fact]
    public async Task Prune_DeletesSamplesAndEventsOlderThanRetention()
    {
        var store = CreateStore();
        var now = T0.AddDays(40);
        await store.AppendSamplesAsync(Ups1, [Sample(now.AddDays(-31)), Sample(now.AddDays(-29))]);
        await store.AppendEventAsync(new HistoryEvent(now.AddDays(-31), Ups1, HistoryEventKind.OnBattery));
        await store.AppendEventAsync(new HistoryEvent(now.AddDays(-29), Ups1, HistoryEventKind.OnLine));

        await store.PruneAsync(now, TimeSpan.FromDays(30));

        var samples = await store.GetSamplesAsync(Ups1, now.AddDays(-100), now, null);
        var events = await store.GetEventsAsync(Ups1, now.AddDays(-100), now, null, 10);
        Assert.Equal(now.AddDays(-29), Assert.Single(samples).Timestamp);
        Assert.Equal(HistoryEventKind.OnLine, Assert.Single(events).Kind);
    }

    [Fact]
    public async Task Prune_DownsamplesRawSamplesOlderThan24h_IntoOneMinuteAverages()
    {
        var store = CreateStore();
        var now = new DateTimeOffset(2026, 3, 10, 12, 0, 30, TimeSpan.Zero);
        var oldMinute = new DateTimeOffset(2026, 3, 8, 6, 15, 0, TimeSpan.Zero); // well beyond 24 h
        var recent = now.AddHours(-1);
        await store.AppendSamplesAsync(Ups1,
        [
            // Six raw samples inside one old minute.
            Sample(oldMinute, charge: 10, status: "OL"),
            Sample(oldMinute.AddSeconds(10), charge: 20, status: "OL"),
            Sample(oldMinute.AddSeconds(20), charge: 30, status: "OL"),
            Sample(oldMinute.AddSeconds(30), charge: 40, status: "OL"),
            Sample(oldMinute.AddSeconds(40), charge: 50, status: "OL"),
            Sample(oldMinute.AddSeconds(50), charge: 60, status: "OB"),
            // The next old minute holds a single sample.
            Sample(oldMinute.AddSeconds(70), charge: 99),
            // Recent data stays raw.
            Sample(recent, charge: 77),
            Sample(recent.AddSeconds(10), charge: 78),
        ]);
        await store.AppendSamplesAsync(Ups2, [Sample(oldMinute, charge: 5), Sample(oldMinute.AddSeconds(5), charge: 15)]);

        await store.PruneAsync(now, TimeSpan.FromDays(30));

        var loaded = await store.GetSamplesAsync(Ups1, now.AddDays(-30), now, null);
        Assert.Equal(4, loaded.Count);
        Assert.Equal(oldMinute, loaded[0].Timestamp);
        Assert.True(loaded[0].IsAggregated);
        Assert.Equal(35, loaded[0].BatteryCharge);
        Assert.Equal("OB", loaded[0].Status);
        Assert.Equal(99, loaded[1].BatteryCharge);
        Assert.True(loaded[1].IsAggregated);
        Assert.Equal([77d, 78d], loaded.Skip(2).Select(s => s.BatteryCharge!.Value));
        Assert.All(loaded.Skip(2), s => Assert.False(s.IsAggregated));

        var other = Assert.Single(await store.GetSamplesAsync(Ups2, now.AddDays(-30), now, null));
        Assert.Equal(10, other.BatteryCharge);

        // A second run changes nothing.
        await store.PruneAsync(now, TimeSpan.FromDays(30));
        Assert.Equal(4, (await store.GetSamplesAsync(Ups1, now.AddDays(-30), now, null)).Count);
    }

    [Fact]
    public async Task Clear_ForOneUps_KeepsTheOthers()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1, [Sample(T0)]);
        await store.AppendSamplesAsync(Ups2, [Sample(T0)]);
        await store.AppendEventAsync(new HistoryEvent(T0, Ups1, HistoryEventKind.Connected));
        await store.AppendEventAsync(new HistoryEvent(T0, Ups2, HistoryEventKind.Connected));

        await store.ClearAsync(Ups1);

        Assert.Empty(await store.GetSamplesAsync(Ups1, T0, T0, null));
        Assert.Single(await store.GetSamplesAsync(Ups2, T0, T0, null));
        Assert.Equal(Ups2, Assert.Single(await store.GetEventsAsync(null, T0, T0, null, 10)).Ups);
        Assert.Equal(Ups2, Assert.Single(await store.GetKnownUpsAsync()));

        // The cleared UPS can be written again.
        await store.AppendSamplesAsync(Ups1, [Sample(T0)]);
        Assert.Single(await store.GetSamplesAsync(Ups1, T0, T0, null));
    }

    [Fact]
    public async Task Clear_ForAll_RemovesEverything()
    {
        var store = CreateStore();
        await store.AppendSamplesAsync(Ups1, [Sample(T0)]);
        await store.AppendEventAsync(new HistoryEvent(T0, Ups2, HistoryEventKind.Connected));

        await store.ClearAsync(null);

        Assert.Empty(await store.GetSamplesAsync(Ups1, T0, T0, null));
        Assert.Empty(await store.GetEventsAsync(null, T0, T0, null, 10));
        Assert.Empty(await store.GetKnownUpsAsync());
    }

    [Fact]
    public async Task Schema_IsCreatedOnAFreshFile_AndReopenedOnAnExistingOne()
    {
        var first = CreateStore();
        await first.AppendSamplesAsync(Ups1, [Sample(T0)]);
        await first.AppendEventAsync(new HistoryEvent(T0, Ups1, HistoryEventKind.Connected, "x"));
        await first.DisposeAsync();

        Assert.True(File.Exists(DbPath));
        var second = CreateStore();
        Assert.Single(await second.GetSamplesAsync(Ups1, T0, T0, null));
        Assert.Equal("x", Assert.Single(await second.GetEventsAsync(Ups1, T0, T0, null, 10)).Detail);
        await second.AppendSamplesAsync(Ups1, [Sample(T0.AddSeconds(1))]);
        await second.DisposeAsync();

        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", command.ExecuteScalar());
    }

    [Fact]
    public async Task Open_WithANewerSchemaVersion_Throws()
    {
        using (var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
    }

    [Fact]
    public async Task InMemoryDatabase_Works()
    {
        await using var store = new SqliteHistoryStore(":memory:");
        await store.AppendSamplesAsync(Ups1, [Sample(T0)]);

        Assert.Single(await store.GetSamplesAsync(Ups1, T0, T0, null));
    }

    [Fact]
    public async Task UsingAfterDispose_Throws()
    {
        var store = CreateStore();
        await store.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.AppendSamplesAsync(Ups1, [Sample(T0)]));
    }

    [Fact]
    public async Task ConcurrentWritersAndReaders_DoNotThrow_AndLoseNothing()
    {
        var store = CreateStore();
        const int writers = 4;
        const int batches = 25;

        var writeTasks = Enumerable.Range(0, writers).Select(w => Task.Run(async () =>
        {
            for (var b = 0; b < batches; b++)
            {
                var at = T0.AddSeconds((w * batches + b) * 10);
                await store.AppendSamplesAsync(Ups1, [Sample(at), Sample(at.AddSeconds(5))]);
                await store.AppendEventAsync(new HistoryEvent(at, w % 2 == 0 ? Ups1 : Ups2, HistoryEventKind.OnBattery));
            }
        })).ToList();
        var readTasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await store.GetSamplesAsync(Ups1, T0, T0.AddHours(1), 20);
                await store.GetEventsAsync(null, T0, T0.AddHours(1), "battery", 5);
                await store.GetKnownUpsAsync();
            }
        })).ToList();

        await Task.WhenAll(writeTasks.Concat(readTasks));

        var samples = await store.GetSamplesAsync(Ups1, T0, T0.AddHours(1), null);
        Assert.Equal(writers * batches * 2, samples.Count);
        Assert.Equal(writers * batches, (await store.GetEventsAsync(null, T0, T0.AddHours(1), null, 1000)).Count);
    }
}
