using System.Globalization;
using CocoNut.Core.Nut;
using CocoNut.Core.Tests.Fakes;

namespace CocoNut.Core.Tests.Nut;

public class NutClientTests
{
    [Fact]
    public async Task ConnectAsync_ThenGetServerVersionAndProtocolVersion_ReturnsServerLines()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line switch
        {
            "VER" => ["Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/"],
            "NETVER" => ["1.3"],
            _ => ["ERR UNKNOWN-COMMAND"],
        });

        string ver = await fx.Client.GetServerVersionAsync();
        string netver = await fx.Client.GetProtocolVersionAsync();

        Assert.Equal("Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/", ver);
        Assert.Equal("1.3", netver);
        Assert.True(fx.Client.IsConnected);
    }

    [Fact]
    public async Task AuthenticateAsync_SendsUsernameThenPassword_WhenBothProvided()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.AuthenticateAsync("alice", "s3cret");

        Assert.Equal(["USERNAME alice", "PASSWORD s3cret"], fx.Server.ReceivedLines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AuthenticateAsync_SkipsUsername_WhenNullOrEmpty(string? username)
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.AuthenticateAsync(username, "s3cret");

        Assert.Equal(["PASSWORD s3cret"], fx.Server.ReceivedLines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AuthenticateAsync_SkipsPassword_WhenNullOrEmpty(string? password)
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.AuthenticateAsync("alice", password);

        Assert.Equal(["USERNAME alice"], fx.Server.ReceivedLines);
    }

    [Fact]
    public async Task AuthenticateAsync_SendsNothing_WhenUsernameAndPasswordAreNullOrEmpty()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.AuthenticateAsync(null, string.Empty);

        Assert.Empty(fx.Server.ReceivedLines);
    }

    [Fact]
    public async Task AuthenticateAsync_QuotesAndEscapesSpecialCharacters()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.AuthenticateAsync("has space", "quote\"and\\slash");

        Assert.Equal(2, fx.Server.ReceivedLines.Count);
        Assert.Equal(["USERNAME", "has space"], NutResponseParser.Tokenize(fx.Server.ReceivedLines[0]));
        Assert.Equal(["PASSWORD", "quote\"and\\slash"], NutResponseParser.Tokenize(fx.Server.ReceivedLines[1]));
    }

    [Fact]
    public async Task LoginAsync_SendsLoginCommandWithUpsName()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["OK"]);

        await fx.Client.LoginAsync("ups1");

        Assert.Equal(["LOGIN ups1"], fx.Server.ReceivedLines);
    }

    [Fact]
    public async Task GetVarAsync_ReturnsUnquotedValue()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line == "GET VAR ups1 battery.charge"
            ? ["VAR ups1 battery.charge \"100\""]
            : ["ERR UNKNOWN-UPS"]);

        string value = await fx.Client.GetVarAsync("ups1", "battery.charge");

        Assert.Equal("100", value);
    }

    [Fact]
    public async Task GetVarDescriptionAsync_ReturnsUnquotedDescription()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line == "GET DESC ups1 battery.charge"
            ? ["DESC ups1 battery.charge \"Battery charge\""]
            : ["ERR UNKNOWN-UPS"]);

        string description = await fx.Client.GetVarDescriptionAsync("ups1", "battery.charge");

        Assert.Equal("Battery charge", description);
    }

    [Fact]
    public async Task ListVarsAsync_ReturnsAllVariablesAsOrdinalDictionary()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line == "LIST VAR ups1"
            ?
            [
                "BEGIN LIST VAR ups1",
                "VAR ups1 battery.charge \"100\"",
                "VAR ups1 ups.status \"OL\"",
                "END LIST VAR ups1",
            ]
            : ["ERR UNKNOWN-UPS"]);

        IReadOnlyDictionary<string, string> vars = await fx.Client.ListVarsAsync("ups1");

        Assert.Equal(2, vars.Count);
        Assert.Equal("100", vars["battery.charge"]);
        Assert.Equal("OL", vars["ups.status"]);
        Assert.Same(StringComparer.Ordinal, Assert.IsType<Dictionary<string, string>>(vars).Comparer);
    }

    [Fact]
    public async Task ListUpsAsync_ReturnsUpsEntries()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line == "LIST UPS"
            ? ["BEGIN LIST UPS", "UPS ups1 \"Example UPS 1\"", "UPS ups2 \"Example UPS 2\"", "END LIST UPS"]
            : ["ERR UNKNOWN-COMMAND"]);

        IReadOnlyList<NutUpsEntry> ups = await fx.Client.ListUpsAsync();

        Assert.Equal([new NutUpsEntry("ups1", "Example UPS 1"), new NutUpsEntry("ups2", "Example UPS 2")], ups);
    }

    [Fact]
    public async Task ListCommandsAsync_ReturnsCommandNames()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line == "LIST CMD ups1"
            ? ["BEGIN LIST CMD ups1", "CMD ups1 load.off", "CMD ups1 load.on", "END LIST CMD ups1"]
            : ["ERR UNKNOWN-UPS"]);

        IReadOnlyList<string> commands = await fx.Client.ListCommandsAsync("ups1");

        Assert.Equal(["load.off", "load.on"], commands);
    }

    [Fact]
    public async Task GetVarAsync_ServerReturnsErr_ThrowsNutExceptionAndConnectionStaysUsable()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line switch
        {
            "GET VAR ups1 nosuchvar" => ["ERR DATA-STALE"],
            "GET VAR ups1 battery.charge" => ["VAR ups1 battery.charge \"100\""],
            _ => ["ERR UNKNOWN-COMMAND"],
        });

        NutException ex = await Assert.ThrowsAsync<NutException>(() => fx.Client.GetVarAsync("ups1", "nosuchvar"));
        Assert.Equal(NutErrorCode.DataStale, ex.ErrorCode);

        Assert.True(fx.Client.IsConnected);
        string value = await fx.Client.GetVarAsync("ups1", "battery.charge");
        Assert.Equal("100", value);
    }

    [Fact]
    public async Task GetVarAsync_ServerReturnsUnknownErrCode_MapsToUnrecognized()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["ERR SOME-FUTURE-ERROR-CODE"]);

        NutException ex = await Assert.ThrowsAsync<NutException>(() => fx.Client.GetVarAsync("ups1", "x"));

        Assert.Equal(NutErrorCode.Unrecognized, ex.ErrorCode);
        Assert.True(fx.Client.IsConnected);
    }

    [Fact]
    public async Task GetVarAsync_UnexpectedResponseLine_ThrowsUnrecognizedNutException()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ => ["SOMETHING UNEXPECTED"]);

        NutException ex = await Assert.ThrowsAsync<NutException>(() => fx.Client.GetVarAsync("ups1", "x"));

        Assert.Equal(NutErrorCode.Unrecognized, ex.ErrorCode);
    }

    [Fact]
    public async Task GetVarAsync_ServerClosesConnection_ThrowsIOExceptionAndRaisesConnectionLostOnce()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(_ =>
        {
            fx.Server.DropConnection();
            return [];
        });

        int lostCount = 0;
        Exception? lostError = null;
        fx.Client.ConnectionLost += (_, ex) =>
        {
            Interlocked.Increment(ref lostCount);
            lostError = ex;
        };

        await Assert.ThrowsAsync<IOException>(() => fx.Client.GetVarAsync("ups1", "x"));

        Assert.Equal(1, lostCount);
        Assert.NotNull(lostError);
        Assert.False(fx.Client.IsConnected);

        // A second call after the connection died does not raise ConnectionLost again.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.Client.GetVarAsync("ups1", "x"));
        Assert.Equal(1, lostCount);
    }

    [Fact]
    public async Task GetVarAsync_TimesOut_ThrowsTimeoutIOExceptionAndRaisesConnectionLost()
    {
        await using Fixture fx = await ConnectAsync(timeout: TimeSpan.FromMilliseconds(100));
        fx.Server.SetHandler(_ => []); // Never responds.

        int lostCount = 0;
        fx.Client.ConnectionLost += (_, _) => Interlocked.Increment(ref lostCount);

        NutTimeoutException ex = await Assert.ThrowsAsync<NutTimeoutException>(() => fx.Client.GetVarAsync("ups1", "x"));

        Assert.IsAssignableFrom<IOException>(ex);
        Assert.False(fx.Client.IsConnected);
        Assert.Equal(1, lostCount);
    }

    [Fact]
    public async Task GetVarAsync_CallerCancellation_PropagatesOperationCanceledException_ClosesConnectionWithoutConnectionLost()
    {
        await using Fixture fx = await ConnectAsync(timeout: TimeSpan.FromSeconds(5));
        fx.Server.SetResponseDelay(TimeSpan.FromSeconds(5));
        fx.Server.SetHandler(_ => ["VAR ups1 x \"1\""]);

        int lostCount = 0;
        fx.Client.ConnectionLost += (_, _) => Interlocked.Increment(ref lostCount);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fx.Client.GetVarAsync("ups1", "x", cts.Token));

        Assert.False(fx.Client.IsConnected);
        Assert.Equal(0, lostCount);
    }

    [Fact]
    public async Task GetVarAsync_ConcurrentCalls_EachGetsItsOwnCorrectValue()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetResponseDelay(TimeSpan.FromMilliseconds(5));
        fx.Server.SetHandler(line =>
        {
            string variable = line.Split(' ')[3];
            string index = variable["var".Length..];
            return [$"VAR ups1 {variable} \"{index}\""];
        });

        const int count = 20;
        var tasks = new Task<string>[count];
        for (int i = 0; i < count; i++)
        {
            tasks[i] = fx.Client.GetVarAsync("ups1", $"var{i}");
        }

        string[] results = await Task.WhenAll(tasks);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(i.ToString(CultureInfo.InvariantCulture), results[i]);
        }
    }

    [Fact]
    public async Task AuthenticateAsync_ServerRejectsPassword_ExceptionNeverContainsThePlainPassword()
    {
        await using Fixture fx = await ConnectAsync();
        fx.Server.SetHandler(line => line.StartsWith("PASSWORD", StringComparison.Ordinal)
            ? ["ERR INVALID-PASSWORD"]
            : ["OK"]);

        const string secret = "sup3rSecretValue";
        NutException ex = await Assert.ThrowsAsync<NutException>(() => fx.Client.AuthenticateAsync("alice", secret));

        Assert.Equal("PASSWORD ***", ex.Query);
        Assert.DoesNotContain(secret, ex.Message);
        Assert.DoesNotContain(secret, ex.ToString());
    }

    [Fact]
    public async Task DisconnectAsync_SendsLogoutOnce_AndIsIdempotent()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        server.SetHandler(_ => ["OK Goodbye"]);

        var client = new NutClient();
        await client.ConnectAsync("127.0.0.1", server.Port);

        await client.DisconnectAsync();
        await client.DisconnectAsync();

        Assert.False(client.IsConnected);
        Assert.Equal(["LOGOUT"], server.ReceivedLines);
    }

    [Fact]
    public async Task DisconnectAsync_WithoutConnecting_DoesNothing()
    {
        var client = new NutClient();

        await client.DisconnectAsync();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_DisconnectsAndPreventsFurtherUse()
    {
        await using var server = new FakeNutServer();
        await server.StartAsync();
        server.SetHandler(_ => ["OK Goodbye"]);

        var client = new NutClient();
        await client.ConnectAsync("127.0.0.1", server.Port);

        await client.DisposeAsync();

        Assert.False(client.IsConnected);
        Assert.Contains("LOGOUT", server.ReceivedLines);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync("127.0.0.1", server.Port));
    }

    [Fact]
    public async Task GetVarAsync_WhenNotConnected_ThrowsInvalidOperationException()
    {
        var client = new NutClient();

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetVarAsync("ups1", "x"));
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperationException()
    {
        await using Fixture fx = await ConnectAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fx.Client.ConnectAsync("127.0.0.1", fx.Server.Port));
    }

    private static async Task<Fixture> ConnectAsync(TimeSpan? timeout = null)
    {
        var server = new FakeNutServer();
        await server.StartAsync();
        var client = new NutClient(timeout: timeout);
        await client.ConnectAsync("127.0.0.1", server.Port);
        return new Fixture { Server = server, Client = client };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required FakeNutServer Server { get; init; }

        public required NutClient Client { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
