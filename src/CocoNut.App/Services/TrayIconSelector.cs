using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Platform;
using CocoNut.Core.Ups;

namespace CocoNut.App.Services;

/// <summary>
/// Picks the tray/window icon WinNUT would show, replicating <c>WinNUT.vb</c>'s <c>UpdateIcon_NotifyIcon</c>
/// and <c>GetIcon</c> exactly (including their quirks - see the remarks below), and loads it as an Avalonia
/// <see cref="WindowIcon"/> from the assets copied to <c>Assets/Icons/*.ico</c> (<c>avares://CocoNut/Assets/Icons/*.ico</c>,
/// the app's assembly name is <c>CocoNut</c>, see <c>CocoNut.App.csproj</c>'s <c>AssemblyName</c>).
/// </summary>
/// <remarks>
/// <para>
/// WinNUT computes an <c>AppIconIdx</c> bit combination (<c>Common_Enums.vb</c>) and looks its numeric
/// value up in <c>GetIcon</c>'s switch:
/// </para>
/// <list type="bullet">
/// <item><description><c>IDX_BATT_0/25/50/75/100 = 1/2/4/8/16</c> - battery charge bucket (0-10%, 11-25%, 26-39% and 40-50% both map to <c>IDX_BATT_50</c>, 51-75%, 76-100%; see <c>Update_UPS_Data</c>'s <c>Select Case UPS_BattCh</c>).</description></item>
/// <item><description><c>IDX_OL = 32</c> - set when <c>ups.status</c> has <c>OL</c>; when it has <c>OB</c> instead the base index is <c>0</c> (and stays <c>0</c> for any other status, since WinNUT's <c>If/ElseIf</c> has no <c>Else</c> and this port has no persisted previous state to fall back to).</description></item>
/// <item><description><c>WIN_DARK = 64</c> - added when running under the OS/tray dark theme.</description></item>
/// <item><description><c>IDX_ICO_OFFLINE = 128</c> and <c>IDX_ICO_RETRY = 256</c> - override everything above: not connected, or reconnecting.</description></item>
/// <item><description><c>IDX_OFFSET = 1024</c> - always added; the sum is the resource/file index (e.g. <c>1057.ico</c>).</description></item>
/// </list>
/// <para>
/// <c>GetIcon</c> only has cases for the combinations WinNUT can actually produce - the on-battery, dark-theme,
/// 0-25% charge combinations (1089, 1090) are missing and fall through to its <c>Case Else</c>, and
/// <c>Case 1104</c> (on-line... actually on-battery, 100% charge, dark) returns the <b>1096</b> resource instead
/// of its own - both apparent bugs in WinNUT, reproduced here for exact parity. <c>1079.ico</c>/<c>1080.ico</c>
/// exist as assets (and as dead <c>GetIcon</c> cases) but no reachable <c>AppIconIdx</c> combination selects them
/// in WinNUT either.
/// </para>
/// </remarks>
public static class TrayIconSelector
{
    private const int BatteryEmpty = 1;
    private const int BatteryLow = 2;
    private const int BatteryMid = 4;
    private const int BatteryHigh = 8;
    private const int BatteryFull = 16;
    private const int OnLine = 32;
    private const int WinDark = 64;
    private const int Offline = 128;
    private const int Retry = 256;
    private const int Offset = 1024;

    private const string AssetBase = "avares://CocoNut/Assets/Icons/";

    /// <summary>WinNUT's <c>GetIcon</c> switch, keyed by the final (with <see cref="Offset"/>) index. The default arm is WinNUT's <c>Case Else</c>.</summary>
    private static readonly Dictionary<int, string> IconFileByIndex = new()
    {
        [1025] = "1025.ico", // OB, 0%
        [1026] = "1026.ico", // OB, 25%
        [1028] = "1028.ico", // OB, 50%
        [1032] = "1032.ico", // OB, 75%
        [1040] = "1040.ico", // OB, 100%
        [1057] = "1057.ico", // OL, 0%
        [1058] = "1058.ico", // OL, 25%
        [1060] = "1060.ico", // OL, 50%
        [1064] = "1064.ico", // OL, 75%
        [1072] = "1072.ico", // OL, 100%
        [1079] = "1079.ico", // unreachable in WinNUT itself; kept for parity with GetIcon's switch
        [1080] = "1080.ico", // unreachable in WinNUT itself; kept for parity with GetIcon's switch
        [1092] = "1092.ico", // OB, 50%, dark
        [1096] = "1096.ico", // OB, 75%, dark
        [1104] = "1096.ico", // OB, 100%, dark - WinNUT bug: returns the 1096 resource, not 1104's own
        [1121] = "1121.ico", // OL, 0%, dark
        [1122] = "1122.ico", // OL, 25%, dark
        [1124] = "1124.ico", // OL, 50%, dark
        [1128] = "1128.ico", // OL, 75%, dark
        [1136] = "1136.ico", // OL, 100%, dark (also the Case Else default)
        [1152] = "1152.ico", // offline
        [1216] = "1216.ico", // offline, dark
        [1280] = "1280.ico", // retry
        [1344] = "1344.ico", // retry, dark
    };

    private const string DefaultIconFile = "1136.ico";

    private static readonly ConcurrentDictionary<string, WindowIcon> IconCache = new();

    /// <summary>
    /// Picks the <c>avares://CocoNut/Assets/Icons/*.ico</c> URI WinNUT's <c>UpdateIcon_NotifyIcon</c>/<c>GetIcon</c>
    /// would show for the tray icon.
    /// </summary>
    /// <param name="connected">Whether the client currently holds a connection to the NUT server.</param>
    /// <param name="reconnecting">
    /// Whether it is retrying after a lost connection with auto-reconnect enabled (WinNUT's <c>UPS_Lostconnect</c>
    /// with <c>AutoReconnect</c>). Takes priority over <paramref name="connected"/>, matching WinNUT (a
    /// reconnect attempt is not "connected", but also not the plain "offline" icon).
    /// </param>
    /// <param name="status">The last known <c>ups.status</c>; only <see cref="UpsStatus.OL"/>/<see cref="UpsStatus.OB"/> affect the icon.</param>
    /// <param name="batteryCharge">Battery charge in percent (0-100), or <see langword="null"/> when unknown/unavailable (WinNUT's <c>-1</c>).</param>
    /// <param name="darkTheme">Whether the OS/tray area is using a dark theme (WinNUT's <c>WinDarkMode</c>).</param>
    public static string SelectIconAsset(bool connected, bool reconnecting, UpsStatus status, double? batteryCharge, bool darkTheme)
    {
        int index = ComputeAppIconIndex(connected, reconnecting, status, batteryCharge, darkTheme);
        string file = IconFileByIndex.GetValueOrDefault(index, DefaultIconFile);
        return AssetBase + file;
    }

    private static int ComputeAppIconIndex(bool connected, bool reconnecting, UpsStatus status, double? batteryCharge, bool darkTheme)
    {
        int mode = Offset | (darkTheme ? WinDark : 0);

        if (reconnecting)
        {
            return Retry | mode;
        }

        if (!connected)
        {
            return Offline | mode;
        }

        int baseIndex = status.HasFlag(UpsStatus.OL) ? OnLine : 0;
        if (batteryCharge is double charge)
        {
            baseIndex |= BatteryBucket(charge);
        }

        return baseIndex | mode;
    }

    /// <summary>Mirrors <c>WinNUT.vb</c>'s <c>Update_UPS_Data</c> <c>Select Case UPS_BattCh</c> (26-39% and 40-50% both map to <see cref="BatteryMid"/>).</summary>
    private static int BatteryBucket(double chargePercent)
    {
        int rounded = (int)Math.Round(Math.Clamp(chargePercent, 0, 100), MidpointRounding.AwayFromZero);
        return rounded switch
        {
            >= 76 => BatteryFull,
            >= 51 => BatteryHigh,
            >= 26 => BatteryMid,
            >= 11 => BatteryLow,
            _ => BatteryEmpty,
        };
    }

    /// <summary>Loads and caches a <see cref="WindowIcon"/> for an <c>avares://</c> URI such as one returned by <see cref="SelectIconAsset"/>.</summary>
    public static WindowIcon LoadIcon(string uri) =>
        IconCache.GetOrAdd(uri, static u =>
        {
            using Stream stream = AssetLoader.Open(new Uri(u));
            return new WindowIcon(stream);
        });
}
