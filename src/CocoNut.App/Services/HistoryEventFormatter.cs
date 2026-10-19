using CocoNut.Core.History;
using CocoNut.Core.Shutdown;
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

    /// <summary>
    /// The detail text to display. The recorder stores the <see cref="ShutdownReason"/> name for the shutdown events;
    /// for those it is replaced by a localized reason. Any other detail (or a name that is not a defined reason) is shown unchanged.
    /// </summary>
    public static string ToLocalizedDetail(HistoryEventKind kind, string? detail)
    {
        if (detail is null)
        {
            return string.Empty;
        }

        if (kind is HistoryEventKind.ShutdownPending or HistoryEventKind.ShutdownCancelled or HistoryEventKind.ShutdownExecuting
            && Enum.TryParse(detail, ignoreCase: false, out ShutdownReason reason)
            && Enum.IsDefined(reason))
        {
            return reason switch
            {
                ShutdownReason.BatteryChargeFloor => Strings.History_Reason_BatteryChargeFloor,
                ShutdownReason.RuntimeFloor => Strings.History_Reason_RuntimeFloor,
                ShutdownReason.UpsLowBattery => Strings.History_Reason_UpsLowBattery,
                ShutdownReason.ForcedShutdown => Strings.History_Reason_ForcedShutdown,
                ShutdownReason.PowerRestored => Strings.History_Reason_PowerRestored,
                _ => detail,
            };
        }

        return detail;
    }
}
