using CocoNut.Core.Ups;

namespace CocoNut.Core.Tests.Ups;

public class UpsStatusParserTests
{
    [Fact]
    public void Parse_KnownTokens_CombinesFlags()
    {
        var status = UpsStatusParser.Parse("OB LB", out var unknown);

        Assert.Equal(UpsStatus.OB | UpsStatus.LB, status);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Parse_IsCaseInsensitive()
    {
        var status = UpsStatusParser.Parse("ob lb", out var unknown);

        Assert.Equal(UpsStatus.OB | UpsStatus.LB, status);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Parse_AllKnownStatuses_MapToDistinctFlags()
    {
        var status = UpsStatusParser.Parse(
            "OL OB LB HB CHRG DISCHRG FSD BYPASS CAL OFF OVER TRIM BOOST ALARM ECO RB COMMLOST TEST",
            out var unknown);

        Assert.Equal(
            UpsStatus.OL | UpsStatus.OB | UpsStatus.LB | UpsStatus.HB | UpsStatus.CHRG | UpsStatus.DISCHRG |
            UpsStatus.FSD | UpsStatus.BYPASS | UpsStatus.CAL | UpsStatus.OFF | UpsStatus.OVER | UpsStatus.TRIM |
            UpsStatus.BOOST | UpsStatus.ALARM | UpsStatus.ECO | UpsStatus.RB | UpsStatus.COMMLOST | UpsStatus.TEST,
            status);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Parse_UnknownToken_IsIgnoredButReported()
    {
        // WinNUT: a single unknown token made Enum.Parse throw for the whole ups.status value, keeping the
        // previous status. This parser must instead keep the known tokens and only report the unknown one.
        var status = UpsStatusParser.Parse("OB FUTURE-STATUS LB", out var unknown);

        Assert.Equal(UpsStatus.OB | UpsStatus.LB, status);
        Assert.Equal(["FUTURE-STATUS"], unknown);
    }

    [Fact]
    public void Parse_OnlyUnknownTokens_ReturnsNoneAndReportsAll()
    {
        var status = UpsStatusParser.Parse("FOO BAR", out var unknown);

        Assert.Equal(UpsStatus.None, status);
        Assert.Equal(["FOO", "BAR"], unknown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_EmptyOrNull_ReturnsNoneAndNoUnknownTokens(string? raw)
    {
        var status = UpsStatusParser.Parse(raw, out var unknown);

        Assert.Equal(UpsStatus.None, status);
        Assert.Empty(unknown);
    }

    [Fact]
    public void Parse_ExtraWhitespaceBetweenTokens_IsIgnored()
    {
        var status = UpsStatusParser.Parse("  OB   LB  ", out var unknown);

        Assert.Equal(UpsStatus.OB | UpsStatus.LB, status);
        Assert.Empty(unknown);
    }
}
