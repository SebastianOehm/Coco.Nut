using CocoNut.Core.Logging;
using CocoNut.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Tests.Logging;

public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Log_WhenEnabled_WritesFormattedLineToTodaysFile()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true, minimumLevel: LogLevel.Information);
        var logger = provider.CreateLogger("CocoNut.Core.Tests.MyCategory");

        logger.LogInformation("hello from the test");
        provider.Dispose(); // Drains the background writer before we read the file.

        var expectedPath = Path.Combine(_temp.Path, $"coconut-{DateTime.Now:yyyyMMdd}.log");
        Assert.True(File.Exists(expectedPath));
        var line = File.ReadAllText(expectedPath);
        Assert.Contains("[INF]", line);
        Assert.Contains("CocoNut.Core.Tests.MyCategory: hello from the test", line);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INF\]", line.TrimEnd());
    }

    [Fact]
    public void Log_WhenDisabled_WritesNothing()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: false);
        var logger = provider.CreateLogger("Category");

        logger.LogInformation("should not be written");
        provider.Dispose();

        Assert.Empty(Directory.GetFiles(_temp.Path, "coconut-*.log"));
    }

    [Fact]
    public void Log_BelowMinimumLevel_IsFiltered()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true, minimumLevel: LogLevel.Warning);
        var logger = provider.CreateLogger("Category");

        logger.LogInformation("filtered out");
        logger.LogWarning("kept");
        provider.Dispose();

        var text = File.ReadAllText(provider.CurrentLogFilePath);
        Assert.DoesNotContain("filtered out", text);
        Assert.Contains("kept", text);
    }

    [Fact]
    public void ChangingMinimumLevelAtRuntime_TakesEffectImmediately()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true, minimumLevel: LogLevel.Warning);
        var logger = provider.CreateLogger("Category");

        logger.LogInformation("filtered before change");
        provider.MinimumLevel = LogLevel.Information;
        logger.LogInformation("kept after change");
        provider.Dispose();

        var text = File.ReadAllText(provider.CurrentLogFilePath);
        Assert.DoesNotContain("filtered before change", text);
        Assert.Contains("kept after change", text);
    }

    [Fact]
    public void DisablingAtRuntime_StopsFurtherWrites()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true);
        var logger = provider.CreateLogger("Category");

        logger.LogInformation("written while enabled");
        provider.Enabled = false;
        logger.LogInformation("not written while disabled");
        provider.Dispose();

        var text = File.ReadAllText(provider.CurrentLogFilePath);
        Assert.Contains("written while enabled", text);
        Assert.DoesNotContain("not written while disabled", text);
    }

    [Fact]
    public void Log_WithException_AppendsExceptionDetails()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true);
        var logger = provider.CreateLogger("Category");

        logger.LogError(new InvalidOperationException("boom"), "operation failed");
        provider.Dispose();

        var text = File.ReadAllText(provider.CurrentLogFilePath);
        Assert.Contains("operation failed", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
    }

    [Fact]
    public void DeleteLogFiles_RemovesAllLogFilesInTheDirectory()
    {
        using var provider = new FileLoggerProvider(_temp.Path, enabled: true);
        provider.CreateLogger("Category").LogInformation("some content");
        // Force the background writer to create the file before we assert on it.
        SpinUntilFileExists(provider.CurrentLogFilePath);

        provider.DeleteLogFiles();

        Assert.Empty(Directory.GetFiles(_temp.Path, "coconut-*.log"));
    }

    [Fact]
    public void Dispose_FlushesPendingWrites()
    {
        var provider = new FileLoggerProvider(_temp.Path, enabled: true);
        var logger = provider.CreateLogger("Category");
        logger.LogInformation("flushed on dispose");

        provider.Dispose();

        Assert.Contains("flushed on dispose", File.ReadAllText(provider.CurrentLogFilePath));
    }

    [Fact]
    public void Retention_KeepsOnlyTheMostRecentSevenFiles()
    {
        // Simulate 9 pre-existing older daily files (using a year far in the past so they always sort before
        // "today"'s file, regardless of when this test runs).
        for (var day = 1; day <= 9; day++)
        {
            File.WriteAllText(Path.Combine(_temp.Path, $"coconut-202001{day:D2}.log"), "old");
        }

        using var provider = new FileLoggerProvider(_temp.Path, enabled: true);
        provider.CreateLogger("Category").LogInformation("triggers rollover/retention");
        provider.Dispose();

        var remaining = Directory.GetFiles(_temp.Path, "coconut-*.log")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(7, remaining.Length);
        // The 3 oldest (01-03) should have been deleted; today's file plus 04-09 remain.
        Assert.DoesNotContain("coconut-20200101.log", remaining);
        Assert.DoesNotContain("coconut-20200102.log", remaining);
        Assert.DoesNotContain("coconut-20200103.log", remaining);
        Assert.Contains("coconut-20200109.log", remaining);
        Assert.Contains(Path.GetFileName(provider.CurrentLogFilePath), remaining);
    }

    private static void SpinUntilFileExists(string path)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
    }
}
