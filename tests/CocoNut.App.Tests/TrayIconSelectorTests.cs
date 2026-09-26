using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using CocoNut.App.Services;
using CocoNut.Core.Ups;

namespace CocoNut.App.Tests;

public class TrayIconSelectorTests
{
    // Expected files decoded from WinNUT.vb's UpdateIcon_NotifyIcon/GetIcon and Common_Enums.vb's
    // AppIconIdx (see the remarks on TrayIconSelector). Battery percentages are chosen at, or just
    // inside, WinNUT's bucket boundaries (0-10, 11-25, 26-50, 51-75, 76-100).
    [Theory]
    [InlineData(false, false, UpsStatus.None, null, false, "1152.ico")] // offline, light
    [InlineData(false, false, UpsStatus.None, null, true, "1216.ico")] // offline, dark
    [InlineData(false, true, UpsStatus.None, null, false, "1280.ico")] // reconnecting, light
    [InlineData(false, true, UpsStatus.None, null, true, "1344.ico")] // reconnecting, dark
    [InlineData(true, true, UpsStatus.OL, 100.0, false, "1280.ico")] // reconnecting wins over connected/status/charge
    [InlineData(true, true, UpsStatus.OL, 100.0, true, "1344.ico")] // reconnecting wins, dark
    [InlineData(true, false, UpsStatus.OL, 0.0, false, "1057.ico")] // online, 0%
    [InlineData(true, false, UpsStatus.OL, 20.0, false, "1058.ico")] // online, 25% bucket (11-25)
    [InlineData(true, false, UpsStatus.OL, 45.0, false, "1060.ico")] // online, 50% bucket (26-50)
    [InlineData(true, false, UpsStatus.OL, 70.0, false, "1064.ico")] // online, 75% bucket (51-75)
    [InlineData(true, false, UpsStatus.OL, 100.0, false, "1072.ico")] // online, 100%
    [InlineData(true, false, UpsStatus.OL, 0.0, true, "1121.ico")] // online, 0%, dark
    [InlineData(true, false, UpsStatus.OL, 25.0, true, "1122.ico")] // online, 25% bucket, dark
    [InlineData(true, false, UpsStatus.OL, 50.0, true, "1124.ico")] // online, 50% bucket, dark
    [InlineData(true, false, UpsStatus.OL, 75.0, true, "1128.ico")] // online, 75% bucket, dark
    [InlineData(true, false, UpsStatus.OL, 100.0, true, "1136.ico")] // online, 100%, dark
    [InlineData(true, false, UpsStatus.OB, 0.0, false, "1025.ico")] // on battery, 0%
    [InlineData(true, false, UpsStatus.OB, 25.0, false, "1026.ico")] // on battery, 25% bucket
    [InlineData(true, false, UpsStatus.OB, 50.0, false, "1028.ico")] // on battery, 50% bucket
    [InlineData(true, false, UpsStatus.OB, 75.0, false, "1032.ico")] // on battery, 75% bucket
    [InlineData(true, false, UpsStatus.OB, 100.0, false, "1040.ico")] // on battery, 100%
    [InlineData(true, false, UpsStatus.OB, 50.0, true, "1092.ico")] // on battery, 50% bucket, dark
    [InlineData(true, false, UpsStatus.OB, 75.0, true, "1096.ico")] // on battery, 75% bucket, dark
    [InlineData(true, false, UpsStatus.OB, 100.0, true, "1096.ico")] // WinNUT bug: GetIcon's "Case 1104" returns the 1096 resource, not 1104's own
    [InlineData(true, false, UpsStatus.OB, 0.0, true, "1136.ico")] // WinNUT gap: index 1089 has no GetIcon case, falls to its Case Else default
    [InlineData(true, false, UpsStatus.OB, 25.0, true, "1136.ico")] // WinNUT gap: index 1090 has no GetIcon case, falls to its Case Else default
    [InlineData(true, false, UpsStatus.None, null, false, "1136.ico")] // connected, no ups.status/charge data yet -> default
    [InlineData(true, false, UpsStatus.OB, null, false, "1136.ico")] // connected, on battery, charge unknown -> default (WinNUT has no case for a plain OB/OL index)
    public void SelectIconAsset_matches_WinNUT_mapping(bool connected, bool reconnecting, UpsStatus status, double? batteryCharge, bool darkTheme, string expectedFile)
    {
        string asset = TrayIconSelector.SelectIconAsset(connected, reconnecting, status, batteryCharge, darkTheme);

        Assert.Equal($"avares://CocoNut/Assets/Icons/{expectedFile}", asset);
    }

    [AvaloniaFact]
    public void SelectIconAsset_always_returns_an_asset_that_exists()
    {
        bool[] bools = [false, true];
        UpsStatus[] statuses = [UpsStatus.None, UpsStatus.OL, UpsStatus.OB, UpsStatus.OL | UpsStatus.OB];
        double?[] charges = [null, 0, 5, 10, 11, 25, 26, 39, 40, 50, 51, 75, 76, 100];

        foreach (bool connected in bools)
        {
            foreach (bool reconnecting in bools)
            {
                foreach (UpsStatus status in statuses)
                {
                    foreach (double? charge in charges)
                    {
                        foreach (bool darkTheme in bools)
                        {
                            string asset = TrayIconSelector.SelectIconAsset(connected, reconnecting, status, charge, darkTheme);

                            Assert.True(AssetLoader.Exists(new Uri(asset)), $"Missing asset for {asset}");
                        }
                    }
                }
            }
        }
    }

    [AvaloniaFact]
    public void LoadIcon_returns_a_cached_instance_for_the_same_uri()
    {
        string asset = TrayIconSelector.SelectIconAsset(connected: true, reconnecting: false, UpsStatus.OL, batteryCharge: 100, darkTheme: false);

        WindowIcon first = TrayIconSelector.LoadIcon(asset);
        WindowIcon second = TrayIconSelector.LoadIcon(asset);

        Assert.Same(first, second);
    }

    [AvaloniaFact]
    public void LoadIcon_can_load_every_distinct_asset_in_the_mapping()
    {
        string[] files =
        [
            "1025.ico", "1026.ico", "1028.ico", "1032.ico", "1040.ico",
            "1057.ico", "1058.ico", "1060.ico", "1064.ico", "1072.ico",
            "1092.ico", "1096.ico",
            "1121.ico", "1122.ico", "1124.ico", "1128.ico", "1136.ico",
            "1152.ico", "1216.ico", "1280.ico", "1344.ico",
        ];

        foreach (string file in files)
        {
            WindowIcon icon = TrayIconSelector.LoadIcon($"avares://CocoNut/Assets/Icons/{file}");
            Assert.NotNull(icon);
        }
    }
}
