namespace CocoNut.Core.History;

/// <summary>Persistent store of UPS samples and events. Implementations must be safe to call concurrently.</summary>
public interface IHistoryStore : IAsyncDisposable
{
    /// <summary>Opens the store and creates or migrates its schema. Optional: every other call does it on demand.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="samples"/> for <paramref name="ups"/> in one transaction.</summary>
    Task AppendSamplesAsync(UpsKey ups, IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken = default);

    Task AppendEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Samples of <paramref name="ups"/> in [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>], oldest first. When
    /// <paramref name="maxPoints"/> is set and the range holds more rows, they are averaged into at most that many
    /// equal time buckets (each returned point is then aggregated, with the status of the bucket's last row).
    /// </summary>
    Task<IReadOnlyList<HistorySample>> GetSamplesAsync(
        UpsKey ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, int? maxPoints, CancellationToken cancellationToken = default);

    /// <summary>
    /// Events in [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>], newest first, of one UPS or (null) all.
    /// <paramref name="search"/> matches the kind name or the detail text, case-insensitively.
    /// </summary>
    Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(
        UpsKey? ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, string? search, int limit, CancellationToken cancellationToken = default);

    /// <summary>Every UPS that has history.</summary>
    Task<IReadOnlyList<UpsKey>> GetKnownUpsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes samples and events older than <paramref name="retention"/> and downsamples raw samples older than
    /// 24 hours to one averaged sample per minute.
    /// </summary>
    Task PruneAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken = default);

    /// <summary>Deletes the history of one UPS, or of all UPSes when <paramref name="ups"/> is null.</summary>
    Task ClearAsync(UpsKey? ups, CancellationToken cancellationToken = default);
}
