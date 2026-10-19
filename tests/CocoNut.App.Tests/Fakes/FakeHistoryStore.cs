using CocoNut.Core.History;

namespace CocoNut.App.Tests.Fakes;

/// <summary>In-memory <see cref="IHistoryStore"/> that records its queries and can be told to fail.</summary>
public sealed class FakeHistoryStore : IHistoryStore
{
    public List<UpsKey> KnownUps { get; } = [];

    public List<HistorySample> Samples { get; } = [];

    public List<HistoryEvent> Events { get; } = [];

    /// <summary>Thrown by every query and by <see cref="ClearAsync"/> when set.</summary>
    public Exception? Failure { get; set; }

    public List<(UpsKey Ups, DateTimeOffset From, DateTimeOffset To, int? MaxPoints)> SampleQueries { get; } = [];

    public List<(UpsKey? Ups, DateTimeOffset From, DateTimeOffset To, string? Search, int Limit)> EventQueries { get; } = [];

    public List<UpsKey?> Cleared { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AppendSamplesAsync(UpsKey ups, IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken = default)
    {
        Samples.AddRange(samples);
        return Task.CompletedTask;
    }

    public Task AppendEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(historyEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistorySample>> GetSamplesAsync(
        UpsKey ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, int? maxPoints, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SampleQueries.Add((ups, rangeStart, rangeEnd, maxPoints));
        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<HistorySample>>(Failure);
        }

        return Task.FromResult<IReadOnlyList<HistorySample>>(
            Samples.Where(s => s.Timestamp >= rangeStart && s.Timestamp <= rangeEnd).OrderBy(s => s.Timestamp).ToList());
    }

    public Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(
        UpsKey? ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, string? search, int limit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EventQueries.Add((ups, rangeStart, rangeEnd, search, limit));
        if (Failure is not null)
        {
            return Task.FromException<IReadOnlyList<HistoryEvent>>(Failure);
        }

        return Task.FromResult<IReadOnlyList<HistoryEvent>>(
            Events.Where(e => e.Timestamp >= rangeStart && e.Timestamp <= rangeEnd).OrderByDescending(e => e.Timestamp).Take(limit).ToList());
    }

    public Task<IReadOnlyList<UpsKey>> GetKnownUpsAsync(CancellationToken cancellationToken = default) =>
        Failure is not null
            ? Task.FromException<IReadOnlyList<UpsKey>>(Failure)
            : Task.FromResult<IReadOnlyList<UpsKey>>(KnownUps.ToList());

    public Task PruneAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ClearAsync(UpsKey? ups, CancellationToken cancellationToken = default)
    {
        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        Cleared.Add(ups);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
