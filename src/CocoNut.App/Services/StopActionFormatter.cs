using CocoNut.Core.Abstractions;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Turns a <see cref="StopAction"/> into its localized display name (the <c>StopAction_*</c> resources), shared
/// by the shutdown countdown window, the main window's event log and the Settings window's stop-action selector
/// so the same enum always reads the same way everywhere.
/// </summary>
public static class StopActionFormatter
{
    /// <summary>Localized text for <paramref name="action"/> (an unrecognized value falls back to Shutdown's text).</summary>
    public static string ToLocalizedText(StopAction action) => action switch
    {
        StopAction.Suspend => Strings.StopAction_Suspend,
        StopAction.Hibernate => Strings.StopAction_Hibernate,
        _ => Strings.StopAction_Shutdown,
    };
}
