using System.Collections.Generic;

namespace CocoNut.Core.Logging;

/// <summary>
/// Thread-safe, fixed-capacity ring buffer of the most recent <see cref="LogEntry"/> values, backing the
/// in-app log view the way WinNUT's <c>Logger.DisplayedLogs</c> queue (default cap 200, WinNUT's <c>MaxEvents</c>)
/// did. Adding past <see cref="Capacity"/> silently discards the oldest entry.
/// </summary>
public sealed class LogBuffer
{
    /// <summary>Default capacity, matching WinNUT's <c>Logger.MaxEvents</c> default.</summary>
    public const int DefaultCapacity = 200;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _entries;

    /// <summary>Maximum number of entries kept. Fixed for the lifetime of the buffer.</summary>
    public int Capacity { get; }

    /// <summary>Raised after an entry is added, on the calling thread. Handlers should not block.</summary>
    public event EventHandler<LogEntry>? EntryAdded;

    public LogBuffer(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        Capacity = capacity;
        _entries = new Queue<LogEntry>(capacity);
    }

    /// <summary>Appends <paramref name="entry"/>, discarding the oldest one first if the buffer is full.</summary>
    public void Add(LogEntry entry)
    {
        lock (_gate)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }

        EntryAdded?.Invoke(this, entry);
    }

    /// <summary>Returns a point-in-time copy of the buffered entries, oldest first.</summary>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>Removes every buffered entry. Does not raise <see cref="EntryAdded"/>.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
