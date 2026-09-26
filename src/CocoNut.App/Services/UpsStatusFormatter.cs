using CocoNut.Core.Ups;
using CocoNut.Localization;

namespace CocoNut.App.Services;

/// <summary>
/// Turns a <see cref="UpsStatus"/> bitmask into a readable, localized list of active flags for the main
/// window's event log list (WinNUT's <c>WinNUT.vb</c> showed the raw <c>ups.status</c> tokens; this resolves
/// each one through the <c>UpsStatus_*</c> resources instead).
/// </summary>
public static class UpsStatusFormatter
{
    /// <summary>Declaration order of <see cref="UpsStatus"/>, used so the list is always in the same order.</summary>
    private static readonly UpsStatus[] FlagsInOrder =
    [
        UpsStatus.OL, UpsStatus.OB, UpsStatus.LB, UpsStatus.HB, UpsStatus.CHRG, UpsStatus.DISCHRG,
        UpsStatus.FSD, UpsStatus.BYPASS, UpsStatus.CAL, UpsStatus.OFF, UpsStatus.OVER, UpsStatus.TRIM,
        UpsStatus.BOOST, UpsStatus.ALARM, UpsStatus.ECO, UpsStatus.RB, UpsStatus.COMMLOST, UpsStatus.TEST,
    ];

    /// <summary>Returns the localized text of every flag set in <paramref name="status"/>, in declaration order.</summary>
    public static IReadOnlyList<string> ToLocalizedList(UpsStatus status) =>
        [.. FlagsInOrder.Where(flag => status.HasFlag(flag)).Select(GetText)];

    /// <summary>Localized text of a single flag (an unrecognized combination falls back to its <see cref="Enum.ToString()"/>).</summary>
    public static string GetText(UpsStatus flag) => flag switch
    {
        UpsStatus.OL => Strings.UpsStatus_OL,
        UpsStatus.OB => Strings.UpsStatus_OB,
        UpsStatus.LB => Strings.UpsStatus_LB,
        UpsStatus.HB => Strings.UpsStatus_HB,
        UpsStatus.CHRG => Strings.UpsStatus_CHRG,
        UpsStatus.DISCHRG => Strings.UpsStatus_DISCHRG,
        UpsStatus.FSD => Strings.UpsStatus_FSD,
        UpsStatus.BYPASS => Strings.UpsStatus_BYPASS,
        UpsStatus.CAL => Strings.UpsStatus_CAL,
        UpsStatus.OFF => Strings.UpsStatus_OFF,
        UpsStatus.OVER => Strings.UpsStatus_OVER,
        UpsStatus.TRIM => Strings.UpsStatus_TRIM,
        UpsStatus.BOOST => Strings.UpsStatus_BOOST,
        UpsStatus.ALARM => Strings.UpsStatus_ALARM,
        UpsStatus.ECO => Strings.UpsStatus_ECO,
        UpsStatus.RB => Strings.UpsStatus_RB,
        UpsStatus.COMMLOST => Strings.UpsStatus_COMMLOST,
        UpsStatus.TEST => Strings.UpsStatus_TEST,
        _ => flag.ToString(),
    };
}
