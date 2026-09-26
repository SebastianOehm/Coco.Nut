using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Logging;

/// <summary>One line captured by <see cref="LogBuffer"/> for display in the UI's log view.</summary>
public sealed record LogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message);
