using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that appends formatted lines to a daily file (<c>coconut-yyyyMMdd.log</c>)
/// inside a log directory (typically <see cref="AppPaths.LogDirectory"/>), mirroring WinNUT's optional file log
/// (<c>LG_LogToFile</c>/<c>LG_LogLevel</c>). All formatting happens on the caller's thread; the actual file write
/// happens on a single background task fed through a <see cref="Channel{T}"/>, so logging never blocks callers
/// on file I/O. <see cref="Enabled"/> and <see cref="MinimumLevel"/> can be changed at runtime.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Number of most-recent daily log files kept; older ones are deleted after a day rolls over.</summary>
    public const int RetainedFileCount = 7;

    private readonly string _logDirectory;
    private readonly Channel<string> _channel;
    private readonly Task _writerTask;
    private readonly Lock _fileGate = new();

    private StreamWriter? _writer;
    private string? _currentDateStamp;
    private bool _disposed;

    /// <summary>Whether messages are written to disk at all. File logging is off by default, as in WinNUT.</summary>
    public bool Enabled { get; set; }

    /// <summary>Lowest level written to the file. Checked on every log call, so it applies immediately.</summary>
    public LogLevel MinimumLevel { get; set; }

    /// <summary>Full path of the file that would receive the next line (today's file), regardless of <see cref="Enabled"/>.</summary>
    public string CurrentLogFilePath =>
        Path.Combine(_logDirectory, $"coconut-{DateTime.Now:yyyyMMdd}.log");

    /// <param name="logDirectory">Directory the daily log files live in. Created if missing.</param>
    /// <param name="enabled">Initial value of <see cref="Enabled"/>.</param>
    /// <param name="minimumLevel">Initial value of <see cref="MinimumLevel"/>.</param>
    public FileLoggerProvider(string logDirectory, bool enabled = false, LogLevel minimumLevel = LogLevel.Information)
    {
        _logDirectory = logDirectory;
        Enabled = enabled;
        MinimumLevel = minimumLevel;

        Directory.CreateDirectory(_logDirectory);
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        _writerTask = Task.Run(ProcessQueueAsync);
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <summary>Closes the current file (if open) and deletes every log file in the directory.</summary>
    public void DeleteLogFiles()
    {
        lock (_fileGate)
        {
            CloseWriter();

            foreach (var file in EnumerateLogFiles())
            {
                TryDelete(file);
            }
        }
    }

    private void Enqueue(LogLevel level, string category, string message, Exception? exception)
    {
        if (!Enabled || level == LogLevel.None || level < MinimumLevel)
        {
            return;
        }

        _channel.Writer.TryWrite(FormatLine(level, category, message, exception));
    }

    private static string FormatLine(LogLevel level, string category, string message, Exception? exception)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"{timestamp} [{LevelLabel(level)}] {category}: {message}";
        return exception is null ? line : $"{line}{Environment.NewLine}{exception}";
    }

    private static string LevelLabel(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private async Task ProcessQueueAsync()
    {
        await foreach (var line in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_fileGate)
            {
                if (_disposed)
                {
                    break;
                }

                EnsureWriter();
                _writer!.WriteLine(line);
                _writer.Flush();
            }
        }
    }

    /// <summary>Opens today's file, rolling over from any previously open one and applying retention.</summary>
    private void EnsureWriter()
    {
        var today = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (_writer is not null && _currentDateStamp == today)
        {
            return;
        }

        CloseWriter();

        _currentDateStamp = today;
        var path = Path.Combine(_logDirectory, $"coconut-{today}.log");
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(stream) { AutoFlush = false };

        ApplyRetention();
    }

    private void CloseWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
        _currentDateStamp = null;
    }

    private void ApplyRetention()
    {
        var toDelete = EnumerateLogFiles()
            .OrderByDescending(path => path, StringComparer.Ordinal) // "coconut-yyyyMMdd.log" sorts chronologically.
            .Skip(RetainedFileCount);

        foreach (var path in toDelete)
        {
            TryDelete(path);
        }
    }

    private IEnumerable<string> EnumerateLogFiles() =>
        Directory.Exists(_logDirectory) ? Directory.EnumerateFiles(_logDirectory, "coconut-*.log") : [];

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a file locked by another process/handle is left for the next cleanup pass.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _channel.Writer.Complete();
        try
        {
            _writerTask.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Best effort drain; the file is flushed/closed below regardless.
        }

        lock (_fileGate)
        {
            _disposed = true;
            CloseWriter();
        }
    }

    private sealed class Logger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            provider.Enabled && logLevel != LogLevel.None && logLevel >= provider.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Enqueue(logLevel, category, formatter(state, exception), exception);
        }
    }
}
