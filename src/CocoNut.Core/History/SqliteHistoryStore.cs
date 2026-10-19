using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace CocoNut.Core.History;

/// <summary>
/// <see cref="IHistoryStore"/> on a local SQLite database (plain ADO.NET). Timestamps are stored as UTC unix
/// milliseconds, the schema version in <c>PRAGMA user_version</c>.
/// </summary>
/// <remarks>
/// Concurrency: one connection, serialized by a <see cref="SemaphoreSlim"/>, instead of a connection per call.
/// SQLite allows a single writer anyway, the recorder writes a few rows a minute so readers never wait long, and a
/// single connection is the only way an in-memory database (<c>:memory:</c>, used by tests) survives between calls.
/// The synchronous ADO.NET work runs on the thread pool so callers on the UI or monitor thread are never blocked.
/// The connection is opened lazily, so constructing the store never throws.
/// </remarks>
public sealed class SqliteHistoryStore : IHistoryStore, IDisposable
{
    /// <summary>Raw samples older than this are downsampled by <see cref="PruneAsync"/>.</summary>
    internal static readonly TimeSpan DownsampleAge = TimeSpan.FromHours(24);

    /// <summary>Length of the buckets raw samples are downsampled into.</summary>
    internal const long DownsampleBucketMs = 60_000;

    private const long SchemaVersion = 1;

    // Each entry migrates the database from version (index) to version (index + 1).
    private static readonly Action<SqliteConnection, SqliteTransaction>[] Migrations = [MigrateToVersion1];

    private const string SampleColumns =
        "battery_charge, battery_voltage, battery_runtime, input_voltage, input_frequency, output_voltage, load_percent, output_power";

    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<UpsKey, long> _upsIds = [];
    private SqliteConnection? _connection;
    private bool _disposed;

    /// <param name="databasePath">Path of the database file (created with its directory if missing), or <c>:memory:</c>.</param>
    public SqliteHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // No pooling: Dispose really closes the file (needed to delete or move it afterwards).
            Pooling = false,
        }.ToString();

        if (databasePath != ":memory:")
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        RunAsync(_ => 0, cancellationToken);

    /// <inheritdoc />
    public Task AppendSamplesAsync(UpsKey ups, IReadOnlyList<HistorySample> samples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ups);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            return Task.CompletedTask;
        }

        return RunAsync(connection =>
        {
            var upsId = GetOrCreateUpsId(connection, ups);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                $"INSERT INTO samples (ups_id, ts, aggregated, status, {SampleColumns}) " +
                "VALUES ($ups, $ts, $agg, $status, $charge, $battV, $runtime, $inV, $freq, $outV, $load, $power)";
            var upsParam = command.Parameters.Add("$ups", SqliteType.Integer);
            var tsParam = command.Parameters.Add("$ts", SqliteType.Integer);
            var aggParam = command.Parameters.Add("$agg", SqliteType.Integer);
            var statusParam = command.Parameters.Add("$status", SqliteType.Text);
            var chargeParam = command.Parameters.Add("$charge", SqliteType.Real);
            var battVParam = command.Parameters.Add("$battV", SqliteType.Real);
            var runtimeParam = command.Parameters.Add("$runtime", SqliteType.Real);
            var inVParam = command.Parameters.Add("$inV", SqliteType.Real);
            var freqParam = command.Parameters.Add("$freq", SqliteType.Real);
            var outVParam = command.Parameters.Add("$outV", SqliteType.Real);
            var loadParam = command.Parameters.Add("$load", SqliteType.Real);
            var powerParam = command.Parameters.Add("$power", SqliteType.Real);

            foreach (var sample in samples)
            {
                upsParam.Value = upsId;
                tsParam.Value = sample.Timestamp.ToUnixTimeMilliseconds();
                aggParam.Value = sample.IsAggregated ? 1 : 0;
                statusParam.Value = sample.Status;
                chargeParam.Value = ToDb(sample.BatteryCharge);
                battVParam.Value = ToDb(sample.BatteryVoltage);
                runtimeParam.Value = ToDb(sample.BatteryRuntimeSeconds);
                inVParam.Value = ToDb(sample.InputVoltage);
                freqParam.Value = ToDb(sample.InputFrequency);
                outVParam.Value = ToDb(sample.OutputVoltage);
                loadParam.Value = ToDb(sample.LoadPercent);
                powerParam.Value = ToDb(sample.OutputPowerWatts);
                command.ExecuteNonQuery();
            }

            transaction.Commit();
            return 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task AppendEventAsync(HistoryEvent historyEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(historyEvent);

        return RunAsync(connection =>
        {
            var upsId = GetOrCreateUpsId(connection, historyEvent.Ups);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO events (ups_id, ts, kind, detail) VALUES ($ups, $ts, $kind, $detail)";
            command.Parameters.AddWithValue("$ups", upsId);
            command.Parameters.AddWithValue("$ts", historyEvent.Timestamp.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$kind", (int)historyEvent.Kind);
            command.Parameters.AddWithValue("$detail", (object?)historyEvent.Detail ?? DBNull.Value);
            command.ExecuteNonQuery();
            return 0;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HistorySample>> GetSamplesAsync(
        UpsKey ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, int? maxPoints, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ups);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoints ?? 1, 1, nameof(maxPoints));

        return RunAsync<IReadOnlyList<HistorySample>>(connection =>
        {
            if (!TryGetUpsId(connection, ups, out var upsId))
            {
                return [];
            }

            var fromMs = rangeStart.ToUnixTimeMilliseconds();
            var toMs = rangeEnd.ToUnixTimeMilliseconds();

            var bucketMs = 0L;
            if (maxPoints is { } limit)
            {
                using var count = connection.CreateCommand();
                count.CommandText = "SELECT COUNT(*) FROM samples WHERE ups_id = $ups AND ts >= $from AND ts <= $to";
                count.Parameters.AddWithValue("$ups", upsId);
                count.Parameters.AddWithValue("$from", fromMs);
                count.Parameters.AddWithValue("$to", toMs);
                if ((long)count.ExecuteScalar()! > limit)
                {
                    // Smallest bucket width for which (ts - from) / width stays below maxPoints.
                    bucketMs = Math.Max(1, (toMs - fromMs + limit) / limit);
                }
            }

            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$ups", upsId);
            command.Parameters.AddWithValue("$from", fromMs);
            command.Parameters.AddWithValue("$to", toMs);

            if (bucketMs == 0)
            {
                command.CommandText =
                    $"SELECT ts, aggregated, status, {SampleColumns} FROM samples " +
                    "WHERE ups_id = $ups AND ts >= $from AND ts <= $to ORDER BY ts";
            }
            else
            {
                // SQLite returns the bare "status" column from the row that holds MAX(ts) of its group.
                command.Parameters.AddWithValue("$bucket", bucketMs);
                command.CommandText =
                    "SELECT CAST(AVG(ts) AS INTEGER) AS bucket_ts, CASE WHEN COUNT(*) > 1 OR SUM(aggregated) > 0 THEN 1 ELSE 0 END, " +
                    "status, AVG(battery_charge), AVG(battery_voltage), AVG(battery_runtime), AVG(input_voltage), " +
                    "AVG(input_frequency), AVG(output_voltage), AVG(load_percent), AVG(output_power), MAX(ts) " +
                    "FROM samples WHERE ups_id = $ups AND ts >= $from AND ts <= $to " +
                    "GROUP BY (ts - $from) / $bucket ORDER BY bucket_ts";
            }

            var result = new List<HistorySample>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new HistorySample(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    reader.GetString(2),
                    ReadDouble(reader, 3),
                    ReadDouble(reader, 4),
                    ReadDouble(reader, 5),
                    ReadDouble(reader, 6),
                    ReadDouble(reader, 7),
                    ReadDouble(reader, 8),
                    ReadDouble(reader, 9),
                    ReadDouble(reader, 10),
                    reader.GetInt64(1) != 0));
            }

            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HistoryEvent>> GetEventsAsync(
        UpsKey? ups, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, string? search, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return RunAsync<IReadOnlyList<HistoryEvent>>(connection =>
        {
            var sql = new StringBuilder(
                "SELECT e.ts, e.kind, e.detail, u.host, u.port, u.name FROM events e JOIN ups u ON u.id = e.ups_id " +
                "WHERE e.ts >= $from AND e.ts <= $to");

            using var command = connection.CreateCommand();
            command.Parameters.AddWithValue("$from", rangeStart.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", rangeEnd.ToUnixTimeMilliseconds());

            if (ups is not null)
            {
                if (!TryGetUpsId(connection, ups, out var upsId))
                {
                    return [];
                }

                sql.Append(" AND e.ups_id = $ups");
                command.Parameters.AddWithValue("$ups", upsId);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                // LIKE is case-insensitive for ASCII; the kind is stored as a number, so match its name here.
                var term = search.Trim();
                var kinds = Enum.GetValues<HistoryEventKind>()
                    .Where(kind => kind.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Select(kind => ((int)kind).ToString(CultureInfo.InvariantCulture));
                sql.Append(" AND (e.detail LIKE $search ESCAPE '\\'");
                var kindList = string.Join(',', kinds);
                if (kindList.Length > 0)
                {
                    sql.Append(" OR e.kind IN (").Append(kindList).Append(')');
                }

                sql.Append(')');
                command.Parameters.AddWithValue(
                    "$search", "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
            }

            sql.Append(" ORDER BY e.ts DESC, e.id DESC LIMIT $limit");
            command.Parameters.AddWithValue("$limit", limit);
            command.CommandText = sql.ToString();

            var result = new List<HistoryEvent>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new HistoryEvent(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    new UpsKey(reader.GetString(3), reader.GetInt32(4), reader.GetString(5)),
                    (HistoryEventKind)reader.GetInt32(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }

            return result;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UpsKey>> GetKnownUpsAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<UpsKey>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT host, port, name FROM ups ORDER BY host, port, name";
            var result = new List<UpsKey>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new UpsKey(reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
            }

            return result;
        }, cancellationToken);

    /// <inheritdoc />
    public Task PruneAsync(DateTimeOffset now, TimeSpan retention, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            var retentionCutoff = (now - retention).ToUnixTimeMilliseconds();
            // Aligned to a bucket boundary so a minute is never split between a downsampled and a raw part,
            // which would leave two aggregated rows for it after the next run.
            var downsampleCutoff = (now - DownsampleAge).ToUnixTimeMilliseconds();
            downsampleCutoff -= downsampleCutoff % DownsampleBucketMs;

            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, "DELETE FROM samples WHERE ts < $cutoff", ("$cutoff", retentionCutoff));
            Execute(connection, transaction, "DELETE FROM events WHERE ts < $cutoff", ("$cutoff", retentionCutoff));

            // MAX(ts) in the inner query makes the bare "status" column the last status of each minute.
            Execute(
                connection,
                transaction,
                $"INSERT INTO samples (ups_id, ts, aggregated, status, {SampleColumns}) " +
                $"SELECT ups_id, bucket_ts, 1, status, {SampleColumns} FROM (" +
                "SELECT ups_id, (ts / $bucket) * $bucket AS bucket_ts, status, MAX(ts) AS last_ts, " +
                "AVG(battery_charge) AS battery_charge, AVG(battery_voltage) AS battery_voltage, " +
                "AVG(battery_runtime) AS battery_runtime, AVG(input_voltage) AS input_voltage, " +
                "AVG(input_frequency) AS input_frequency, AVG(output_voltage) AS output_voltage, " +
                "AVG(load_percent) AS load_percent, AVG(output_power) AS output_power " +
                "FROM samples WHERE aggregated = 0 AND ts < $cutoff GROUP BY ups_id, ts / $bucket)",
                ("$cutoff", downsampleCutoff),
                ("$bucket", DownsampleBucketMs));
            Execute(connection, transaction, "DELETE FROM samples WHERE aggregated = 0 AND ts < $cutoff", ("$cutoff", downsampleCutoff));

            transaction.Commit();
            return 0;
        }, cancellationToken);

    /// <inheritdoc />
    public Task ClearAsync(UpsKey? ups, CancellationToken cancellationToken = default) =>
        RunAsync(connection =>
        {
            long upsId = 0;
            if (ups is not null && !TryGetUpsId(connection, ups, out upsId))
            {
                return 0;
            }

            using var transaction = connection.BeginTransaction();
            if (ups is null)
            {
                Execute(connection, transaction, "DELETE FROM events");
                Execute(connection, transaction, "DELETE FROM samples");
                Execute(connection, transaction, "DELETE FROM ups");
            }
            else
            {
                // Foreign keys cascade to the UPS's samples and events.
                Execute(connection, transaction, "DELETE FROM ups WHERE id = $id", ("$id", upsId));
            }

            transaction.Commit();
            if (ups is null)
            {
                _upsIds.Clear();
            }
            else
            {
                _upsIds.Remove(ups);
            }

            return 0;
        }, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            CloseConnection();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            CloseConnection();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CloseConnection()
    {
        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }

    /// <summary>Runs <paramref name="work"/> on the thread pool with exclusive use of the (lazily opened) connection.</summary>
    private async Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return work(_connection ?? OpenConnection());
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            // WAL lets a reader and the writer proceed together and is crash-safe with synchronous=NORMAL.
            // (On :memory: the journal mode stays "memory"; the pragma is then a no-op.)
            ExecutePragma(connection, "PRAGMA journal_mode = WAL");
            ExecutePragma(connection, "PRAGMA synchronous = NORMAL");
            ExecutePragma(connection, "PRAGMA foreign_keys = ON");
            Migrate(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        _connection = connection;
        return connection;
    }

    private static void Migrate(SqliteConnection connection)
    {
        using var read = connection.CreateCommand();
        read.CommandText = "PRAGMA user_version";
        var version = (long)read.ExecuteScalar()!;

        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"The history database has schema version {version}, newer than the supported version {SchemaVersion}.");
        }

        for (var step = (int)version; step < Migrations.Length; step++)
        {
            using var transaction = connection.BeginTransaction();
            Migrations[step](connection, transaction);
            Execute(connection, transaction, $"PRAGMA user_version = {step + 1}");
            transaction.Commit();
        }
    }

    private static void MigrateToVersion1(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE ups (
              id INTEGER PRIMARY KEY,
              host TEXT NOT NULL,
              port INTEGER NOT NULL,
              name TEXT NOT NULL,
              UNIQUE (host, port, name)
            );
            CREATE TABLE samples (
              ups_id INTEGER NOT NULL REFERENCES ups(id) ON DELETE CASCADE,
              ts INTEGER NOT NULL,
              aggregated INTEGER NOT NULL DEFAULT 0,
              status TEXT NOT NULL,
              battery_charge REAL,
              battery_voltage REAL,
              battery_runtime REAL,
              input_voltage REAL,
              input_frequency REAL,
              output_voltage REAL,
              load_percent REAL,
              output_power REAL
            );
            CREATE INDEX ix_samples_ups_ts ON samples (ups_id, ts);
            CREATE TABLE events (
              id INTEGER PRIMARY KEY,
              ups_id INTEGER NOT NULL REFERENCES ups(id) ON DELETE CASCADE,
              ts INTEGER NOT NULL,
              kind INTEGER NOT NULL,
              detail TEXT
            );
            CREATE INDEX ix_events_ups_ts ON events (ups_id, ts);
            CREATE INDEX ix_events_ts ON events (ts);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Host names are stored lower-case so the unique index matches <see cref="UpsKey"/>'s equality.</summary>
    private static string NormalizeHost(string host) => host.ToLowerInvariant();

    private long GetOrCreateUpsId(SqliteConnection connection, UpsKey ups)
    {
        if (_upsIds.TryGetValue(ups, out var cached))
        {
            return cached;
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT OR IGNORE INTO ups (host, port, name) VALUES ($host, $port, $name)";
        insert.Parameters.AddWithValue("$host", NormalizeHost(ups.Host));
        insert.Parameters.AddWithValue("$port", ups.Port);
        insert.Parameters.AddWithValue("$name", ups.UpsName);
        insert.ExecuteNonQuery();

        TryGetUpsId(connection, ups, out var id);
        return id;
    }

    private bool TryGetUpsId(SqliteConnection connection, UpsKey ups, out long id)
    {
        if (_upsIds.TryGetValue(ups, out id))
        {
            return true;
        }

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id FROM ups WHERE host = $host AND port = $port AND name = $name";
        select.Parameters.AddWithValue("$host", NormalizeHost(ups.Host));
        select.Parameters.AddWithValue("$port", ups.Port);
        select.Parameters.AddWithValue("$name", ups.UpsName);
        if (select.ExecuteScalar() is long found)
        {
            _upsIds[ups] = found;
            id = found;
            return true;
        }

        id = 0;
        return false;
    }

    private static void Execute(
        SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        command.ExecuteNonQuery();
    }

    private static void ExecutePragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object ToDb(double? value) => value.HasValue ? value.Value : DBNull.Value;

    private static double? ReadDouble(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
