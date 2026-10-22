namespace CocoNut.Core.Ups;

/// <summary>Static product information of a UPS, read once after login.</summary>
public sealed record UpsInfo(string Manufacturer, string Model, string Serial, string Firmware)
{
    /// <summary>Placeholder <see cref="UpsCalculations.BuildInfo"/> uses for a value the UPS does not report.</summary>
    public const string UnknownValue = "Unknown";

    /// <summary>
    /// Whether the UPS actually reported <paramref name="value"/>: <see langword="false"/> for <see langword="null"/>,
    /// empty or whitespace text and for the <see cref="UnknownValue"/> placeholder (compared case-insensitively).
    /// </summary>
    public static bool IsReported(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !string.Equals(value.Trim(), UnknownValue, StringComparison.OrdinalIgnoreCase);
}
