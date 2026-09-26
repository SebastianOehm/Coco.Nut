using CocoNut.Core.Logging;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Tests.Logging;

public class LogBufferTests
{
    private static LogEntry MakeEntry(int index) =>
        new(DateTimeOffset.UtcNow, LogLevel.Information, "Test", $"message-{index}");

    [Fact]
    public void Constructor_WithNonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogBuffer(-1));
    }

    [Fact]
    public void Add_WithinCapacity_KeepsEverythingInOrder()
    {
        var buffer = new LogBuffer(capacity: 5);

        for (var i = 0; i < 3; i++)
        {
            buffer.Add(MakeEntry(i));
        }

        var snapshot = buffer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["message-0", "message-1", "message-2"], snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Add_PastCapacity_DiscardsOldestFirst()
    {
        var buffer = new LogBuffer(capacity: 3);

        for (var i = 0; i < 5; i++)
        {
            buffer.Add(MakeEntry(i));
        }

        var snapshot = buffer.Snapshot();
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(["message-2", "message-3", "message-4"], snapshot.Select(e => e.Message));
    }

    [Fact]
    public void Add_RaisesEntryAddedForEachEntry()
    {
        var buffer = new LogBuffer(capacity: 10);
        var raised = new List<LogEntry>();
        buffer.EntryAdded += (_, entry) => raised.Add(entry);

        buffer.Add(MakeEntry(1));
        buffer.Add(MakeEntry(2));

        Assert.Equal(2, raised.Count);
        Assert.Equal("message-1", raised[0].Message);
        Assert.Equal("message-2", raised[1].Message);
    }

    [Fact]
    public void Clear_RemovesEverythingWithoutRaisingEntryAdded()
    {
        var buffer = new LogBuffer(capacity: 10);
        buffer.Add(MakeEntry(1));
        var raisedAfterClear = false;
        buffer.EntryAdded += (_, _) => raisedAfterClear = true;

        buffer.Clear();

        Assert.Empty(buffer.Snapshot());
        Assert.False(raisedAfterClear);
    }

    [Fact]
    public void Snapshot_ReturnsAPointInTimeCopy()
    {
        var buffer = new LogBuffer(capacity: 10);
        buffer.Add(MakeEntry(1));

        var snapshot = buffer.Snapshot();
        buffer.Add(MakeEntry(2));

        Assert.Single(snapshot);
    }

    [Fact]
    public void Add_FromManyThreadsConcurrently_NeverExceedsCapacityAndStaysConsistent()
    {
        const int capacity = 200;
        const int threadCount = 8;
        const int perThread = 500;
        var buffer = new LogBuffer(capacity);

        Parallel.For(0, threadCount, t =>
        {
            for (var i = 0; i < perThread; i++)
            {
                buffer.Add(new LogEntry(DateTimeOffset.UtcNow, LogLevel.Information, "Test", $"t{t}-{i}"));
            }
        });

        var snapshot = buffer.Snapshot();
        Assert.Equal(capacity, snapshot.Count);
    }
}
