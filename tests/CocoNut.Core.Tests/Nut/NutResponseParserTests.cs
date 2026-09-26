using CocoNut.Core.Nut;

namespace CocoNut.Core.Tests.Nut;

public class NutResponseParserTests
{
    [Fact]
    public void Tokenize_PlainTokens_SplitsOnWhitespace()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("VAR ups1 battery.charge \"100\"");

        Assert.Equal(["VAR", "ups1", "battery.charge", "100"], tokens);
    }

    [Fact]
    public void Tokenize_QuotedValueWithSpaces_KeepsSpacesInsideOneToken()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("UPS ups1 \"My UPS Description\"");

        Assert.Equal(["UPS", "ups1", "My UPS Description"], tokens);
    }

    [Fact]
    public void Tokenize_EscapedQuote_IsUnescaped()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("DESC ups1 x \"a \\\"quoted\\\" value\"");

        Assert.Equal("a \"quoted\" value", tokens[3]);
    }

    [Fact]
    public void Tokenize_EscapedBackslash_IsUnescaped()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("VAR ups1 x \"C:\\\\path\"");

        Assert.Equal("C:\\path", tokens[3]);
    }

    [Fact]
    public void Tokenize_EmptyQuotedString_ProducesEmptyToken()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("VAR ups1 x \"\"");

        Assert.Equal(4, tokens.Count);
        Assert.Equal(string.Empty, tokens[3]);
    }

    [Fact]
    public void Tokenize_ErrLine_SplitsCodeFromExtraInfo()
    {
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize("ERR INVALID-ARGUMENT some extra info");

        Assert.Equal(["ERR", "INVALID-ARGUMENT", "some", "extra", "info"], tokens);
    }

    [Theory]
    [InlineData("DATA-STALE", NutErrorCode.DataStale)]
    [InlineData("data-stale", NutErrorCode.DataStale)]
    [InlineData("ACCESS-DENIED", NutErrorCode.AccessDenied)]
    [InlineData("UNKNOWN-UPS", NutErrorCode.UnknownUps)]
    [InlineData("VAR-NOT-SUPPORTED", NutErrorCode.VarNotSupported)]
    [InlineData("CMD-NOT-SUPPORTED", NutErrorCode.CmdNotSupported)]
    [InlineData("INVALID-ARGUMENT", NutErrorCode.InvalidArgument)]
    [InlineData("INSTCMD-FAILED", NutErrorCode.InstCmdFailed)]
    [InlineData("SET-FAILED", NutErrorCode.SetFailed)]
    [InlineData("READONLY", NutErrorCode.ReadOnly)]
    [InlineData("TOO-LONG", NutErrorCode.TooLong)]
    [InlineData("FEATURE-NOT-SUPPORTED", NutErrorCode.FeatureNotSupported)]
    [InlineData("FEATURE-NOT-CONFIGURED", NutErrorCode.FeatureNotConfigured)]
    [InlineData("ALREADY-SSL-MODE", NutErrorCode.AlreadySslMode)]
    [InlineData("DRIVER-NOT-CONNECTED", NutErrorCode.DriverNotConnected)]
    [InlineData("ALREADY-LOGGED-IN", NutErrorCode.AlreadyLoggedIn)]
    [InlineData("INVALID-PASSWORD", NutErrorCode.InvalidPassword)]
    [InlineData("ALREADY-SET-PASSWORD", NutErrorCode.AlreadySetPassword)]
    [InlineData("INVALID-USERNAME", NutErrorCode.InvalidUsername)]
    [InlineData("ALREADY-SET-USERNAME", NutErrorCode.AlreadySetUsername)]
    [InlineData("USERNAME-REQUIRED", NutErrorCode.UsernameRequired)]
    [InlineData("PASSWORD-REQUIRED", NutErrorCode.PasswordRequired)]
    [InlineData("UNKNOWN-COMMAND", NutErrorCode.UnknownCommand)]
    [InlineData("INVALID-VALUE", NutErrorCode.InvalidValue)]
    public void MapErrorCode_KnownCode_MapsToExpectedEnumValue(string wireCode, NutErrorCode expected)
    {
        Assert.Equal(expected, NutResponseParser.MapErrorCode(wireCode));
    }

    [Theory]
    [InlineData("SOME-FUTURE-CODE")]
    [InlineData("")]
    [InlineData("TOTALLY-UNKNOWN")]
    public void MapErrorCode_UnknownCode_MapsToUnrecognized(string wireCode)
    {
        Assert.Equal(NutErrorCode.Unrecognized, NutResponseParser.MapErrorCode(wireCode));
    }

    [Fact]
    public void CreateError_BuildsNutExceptionWithMappedCodeAndMaskedQuery()
    {
        NutException ex = NutResponseParser.CreateError("PASSWORD secret123", "ERR INVALID-PASSWORD");

        Assert.Equal(NutErrorCode.InvalidPassword, ex.ErrorCode);
        Assert.Equal("PASSWORD ***", ex.Query);
        Assert.Equal("ERR INVALID-PASSWORD", ex.RawResponse);
        Assert.DoesNotContain("secret123", ex.Message);
    }

    [Theory]
    [InlineData("ups1", "ups1")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("has\"quote", "\"has\\\"quote\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    [InlineData("", "\"\"")]
    public void QuoteArgument_QuotesOnlyWhenNecessary(string input, string expected)
    {
        Assert.Equal(expected, NutResponseParser.QuoteArgument(input));
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("has space")]
    [InlineData("has\"quote and \\backslash")]
    [InlineData("")]
    [InlineData("  leading and trailing  ")]
    public void QuoteArgument_RoundTripsThroughTokenize(string original)
    {
        string quoted = NutResponseParser.QuoteArgument(original);
        IReadOnlyList<string> tokens = NutResponseParser.Tokenize($"PASSWORD {quoted}");

        Assert.Equal(original, tokens[1]);
    }

    [Theory]
    [InlineData("PASSWORD secret", "PASSWORD ***")]
    [InlineData("PASSWORD \"secret with spaces\"", "PASSWORD ***")]
    [InlineData("PASSWORD", "PASSWORD ***")]
    [InlineData("USERNAME alice", "USERNAME alice")]
    [InlineData("GET VAR ups1 battery.charge", "GET VAR ups1 battery.charge")]
    [InlineData("PASSWORDLESS something", "PASSWORDLESS something")]
    public void MaskCommand_MasksOnlyPasswordCommands(string command, string expected)
    {
        Assert.Equal(expected, NutResponseParser.MaskCommand(command));
    }

    [Fact]
    public void TryParseVarLine_ValidLine_ExtractsNameAndValue()
    {
        bool ok = NutResponseParser.TryParseVarLine("VAR ups1 battery.charge \"100\"", out string name, out string value);

        Assert.True(ok);
        Assert.Equal("battery.charge", name);
        Assert.Equal("100", value);
    }

    [Theory]
    [InlineData("DESC ups1 battery.charge \"Battery charge\"")]
    public void TryParseDescLine_ValidLine_ExtractsNameAndDescription(string line)
    {
        bool ok = NutResponseParser.TryParseDescLine(line, out string name, out string description);

        Assert.True(ok);
        Assert.Equal("battery.charge", name);
        Assert.Equal("Battery charge", description);
    }

    [Fact]
    public void TryParseUpsLine_ValidLine_ExtractsNameAndDescription()
    {
        bool ok = NutResponseParser.TryParseUpsLine("UPS ups1 \"Example UPS\"", out string name, out string description);

        Assert.True(ok);
        Assert.Equal("ups1", name);
        Assert.Equal("Example UPS", description);
    }

    [Fact]
    public void TryParseCmdLine_ValidLine_ExtractsName()
    {
        bool ok = NutResponseParser.TryParseCmdLine("CMD ups1 load.off", out string name);

        Assert.True(ok);
        Assert.Equal("load.off", name);
    }

    [Theory]
    [InlineData("VAR ups1 battery.charge \"100\"")]
    [InlineData("BEGIN LIST VAR ups1")]
    [InlineData("VAR")]
    public void TryParseUpsLine_WrongKeywordOrTooFewTokens_ReturnsFalse(string line)
    {
        bool ok = NutResponseParser.TryParseUpsLine(line, out _, out _);

        Assert.False(ok);
    }

    [Theory]
    [InlineData("UPS ups1 \"desc\"")]
    [InlineData("CMD")]
    public void TryParseCmdLine_WrongKeywordOrTooFewTokens_ReturnsFalse(string line)
    {
        bool ok = NutResponseParser.TryParseCmdLine(line, out _);

        Assert.False(ok);
    }
}
