using CocoNut.Core.History;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Turns a <see cref="HistoryEventKind"/> into its localized display name (the <c>History_Event_*</c> resources);
/// the stored kind and the CSV export stay language neutral, only the history window localizes.
/// </summary>
public static class HistoryEventFormatter
{
    /// <summary>Localized text for <paramref name="kind"/> (a value without a resource falls back to its enum name).</summary>
    public static string ToLocalizedText(HistoryEventKind kind) => kind switch
    {
        HistoryEventKind.Connected => Strings.History_Event_Connected,
        HistoryEventKind.ConnectionLost => Strings.History_Event_ConnectionLost,
        HistoryEventKind.OnBattery => Strings.History_Event_OnBattery,
        HistoryEventKind.OnLine => Strings.History_Event_OnLine,
        HistoryEventKind.LowBattery => Strings.History_Event_LowBattery,
        HistoryEventKind.ForcedShutdown => Strings.History_Event_ForcedShutdown,
        HistoryEventKind.ShutdownPending => Strings.History_Event_ShutdownPending,
        HistoryEventKind.ShutdownCancelled => Strings.History_Event_ShutdownCancelled,
        HistoryEventKind.ShutdownExecuting => Strings.History_Event_ShutdownExecuting,
        HistoryEventKind.ShutdownFailed => Strings.History_Event_ShutdownFailed,
        _ => kind.ToString(),
    };
}
