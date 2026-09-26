using CocoNut.Core.Logging;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Tests.Logging;

public class BufferLoggerProviderTests
{
    [Fact]
    public void Log_AtOrAboveMinimumLevel_IsAddedToBuffer()
    {
        var buffer = new LogBuffer();
        var provider = new BufferLoggerProvider(buffer, LogLevel.Warning);
        var logger = provider.CreateLogger("MyCategory");

        logger.LogWarning("something happened");

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("MyCategory", entry.Category);
        Assert.Equal("something happened", entry.Message);
    }

    [Fact]
    public void Log_BelowMinimumLevel_IsNotAddedToBuffer()
    {
        var buffer = new LogBuffer();
        var provider = new BufferLoggerProvider(buffer, LogLevel.Warning);
        var logger = provider.CreateLogger("MyCategory");

        logger.LogInformation("should be filtered out");

        Assert.Empty(buffer.Snapshot());
    }

    [Fact]
    public void ChangingMinimumLevelAtRuntime_TakesEffectImmediately()
    {
        var buffer = new LogBuffer();
        var provider = new BufferLoggerProvider(buffer, LogLevel.Warning);
        var logger = provider.CreateLogger("MyCategory");

        logger.LogInformation("filtered before change");
        provider.MinimumLevel = LogLevel.Information;
        logger.LogInformation("kept after change");

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Equal("kept after change", entry.Message);
    }

    [Fact]
    public void Log_WithException_AppendsExceptionToMessage()
    {
        var buffer = new LogBuffer();
        var provider = new BufferLoggerProvider(buffer, LogLevel.Information);
        var logger = provider.CreateLogger("MyCategory");

        logger.LogError(new InvalidOperationException("boom"), "operation failed");

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Contains("operation failed", entry.Message);
        Assert.Contains("InvalidOperationException", entry.Message);
    }
}
