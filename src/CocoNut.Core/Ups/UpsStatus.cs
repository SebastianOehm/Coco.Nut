namespace CocoNut.Core.Ups;

/// <summary>
/// Status flags reported by the NUT <c>ups.status</c> variable (space separated tokens, e.g. <c>"OB LB"</c>).
/// Values are powers of two so multiple statuses can be combined.
/// </summary>
[Flags]
public enum UpsStatus
{
    None = 0,
    /// <summary>On line (mains power).</summary>
    OL = 1 << 0,
    /// <summary>On battery.</summary>
    OB = 1 << 1,
    /// <summary>Low battery.</summary>
    LB = 1 << 2,
    /// <summary>High battery.</summary>
    HB = 1 << 3,
    /// <summary>Battery charging.</summary>
    CHRG = 1 << 4,
    /// <summary>Battery discharging.</summary>
    DISCHRG = 1 << 5,
    /// <summary>Forced shutdown requested by the NUT server.</summary>
    FSD = 1 << 6,
    /// <summary>On bypass.</summary>
    BYPASS = 1 << 7,
    /// <summary>Runtime calibration.</summary>
    CAL = 1 << 8,
    /// <summary>UPS output is off.</summary>
    OFF = 1 << 9,
    /// <summary>Overloaded.</summary>
    OVER = 1 << 10,
    /// <summary>Trimming incoming voltage.</summary>
    TRIM = 1 << 11,
    /// <summary>Boosting incoming voltage.</summary>
    BOOST = 1 << 12,
    /// <summary>An alarm is active (see <c>ups.alarm</c>).</summary>
    ALARM = 1 << 13,
    /// <summary>eConversion / ECO mode.</summary>
    ECO = 1 << 14,
    /// <summary>Battery needs replacement.</summary>
    RB = 1 << 15,
    /// <summary>Communication with the UPS driver was lost.</summary>
    COMMLOST = 1 << 16,
    /// <summary>Test in progress.</summary>
    TEST = 1 << 17,
}
