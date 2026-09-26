namespace CocoNut.Core.Ups;

/// <summary>
/// Parses the NUT <c>ups.status</c> variable (e.g. <c>"OB LB"</c>) into <see cref="UpsStatus"/> flags.
/// </summary>
/// <remarks>
/// WinNUT (<c>UPS_Device.Retrieve_UPS_Datas</c>) replaced spaces with commas and called
/// <c>[Enum].Parse(GetType(UPS_States), ...)</c>. A single unknown token made the whole parse throw, so the
/// previous status was kept and nothing else in that token list was applied. This parser is tolerant instead:
/// known tokens are combined and unknown tokens are ignored but reported to the caller, so one unexpected token
/// (a future NUT status, a typo forwarded by a driver, ...) does not hide the tokens that were understood.
/// </remarks>
public static class UpsStatusParser
{
    private static readonly Dictionary<string, UpsStatus> KnownTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OL"] = UpsStatus.OL,
        ["OB"] = UpsStatus.OB,
        ["LB"] = UpsStatus.LB,
        ["HB"] = UpsStatus.HB,
        ["CHRG"] = UpsStatus.CHRG,
        ["DISCHRG"] = UpsStatus.DISCHRG,
        ["FSD"] = UpsStatus.FSD,
        ["BYPASS"] = UpsStatus.BYPASS,
        ["CAL"] = UpsStatus.CAL,
        ["OFF"] = UpsStatus.OFF,
        ["OVER"] = UpsStatus.OVER,
        ["TRIM"] = UpsStatus.TRIM,
        ["BOOST"] = UpsStatus.BOOST,
        ["ALARM"] = UpsStatus.ALARM,
        ["ECO"] = UpsStatus.ECO,
        ["RB"] = UpsStatus.RB,
        ["COMMLOST"] = UpsStatus.COMMLOST,
        ["TEST"] = UpsStatus.TEST,
    };

    /// <summary>
    /// Parses <paramref name="raw"/> (whitespace separated, case-insensitive tokens) into <see cref="UpsStatus"/> flags.
    /// </summary>
    /// <param name="raw">The raw <c>ups.status</c> value, e.g. <c>"OB LB"</c>. <see langword="null"/> or empty yields
    /// <see cref="UpsStatus.None"/>.</param>
    /// <param name="unknownTokens">Tokens that did not match a known status, in the order they appeared. Empty when
    /// every token was recognized.</param>
    /// <returns>The combined flags of every recognized token.</returns>
    public static UpsStatus Parse(string? raw, out IReadOnlyList<string> unknownTokens)
    {
        var result = UpsStatus.None;

        if (string.IsNullOrWhiteSpace(raw))
        {
            unknownTokens = Array.Empty<string>();
            return result;
        }

        List<string>? unknown = null;
        foreach (var token in raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (KnownTokens.TryGetValue(token, out var flag))
            {
                result |= flag;
            }
            else
            {
                (unknown ??= []).Add(token);
            }
        }

        unknownTokens = unknown ?? (IReadOnlyList<string>)Array.Empty<string>();
        return result;
    }
}
