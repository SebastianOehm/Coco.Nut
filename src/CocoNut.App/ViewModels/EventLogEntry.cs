using System.Globalization;

namespace CocoNut.App.ViewModels;

/// <summary>
/// One line of the main window's localized, human-readable event log (WinNUT's event log list box), distinct
/// from the English technical log (<see cref="CocoNut.Core.Logging.LogBuffer"/>/file log).
/// </summary>
public sealed record EventLogEntry(DateTimeOffset Timestamp, string Message)
{
    /// <summary>Local time of day, for the list's leading timestamp column.</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
}
