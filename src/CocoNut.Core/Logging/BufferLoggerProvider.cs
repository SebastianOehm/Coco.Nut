using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that writes every accepted log message into a <see cref="LogBuffer"/>, backing
/// the in-app log view. <see cref="MinimumLevel"/> can be changed at runtime (e.g. from a Settings dialog).
/// </summary>
public sealed class BufferLoggerProvider : ILoggerProvider
{
    private readonly LogBuffer _buffer;

    /// <summary>Lowest level accepted into the buffer. Checked on every log call, so it applies immediately.</summary>
    public LogLevel MinimumLevel { get; set; }

    public BufferLoggerProvider(LogBuffer buffer, LogLevel minimumLevel = LogLevel.Information)
    {
        _buffer = buffer;
        MinimumLevel = minimumLevel;
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        // The LogBuffer outlives this provider (it backs the UI); nothing to release here.
    }

    private sealed class Logger(BufferLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            provider._buffer.Add(new LogEntry(DateTimeOffset.Now, logLevel, category, message));
        }
    }
}
