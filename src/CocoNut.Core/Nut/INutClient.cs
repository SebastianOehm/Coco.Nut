namespace CocoNut.Core.Nut;

/// <summary>
/// Client for the NUT network protocol (upsd, default TCP port 3493). One instance wraps one TCP connection.
/// All methods are safe to call concurrently: requests are serialized on the connection.
/// </summary>
/// <remarks>
/// Error contract: server <c>ERR</c> replies throw <see cref="NutException"/> and leave the connection usable.
/// Transport failures (connection refused/closed, timeout, EOF) throw <see cref="IOException"/> (or
/// <see cref="System.Net.Sockets.SocketException"/> on connect), close the connection and raise
/// <see cref="ConnectionLost"/> once.
/// </remarks>
public interface INutClient : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>Raised once when an established connection breaks unexpectedly (not on <see cref="DisconnectAsync"/>).</summary>
    event EventHandler<Exception?>? ConnectionLost;

    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary><c>VER</c> - e.g. "Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/".</summary>
    Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default);

    /// <summary><c>NETVER</c> - e.g. "1.3".</summary>
    Task<string> GetProtocolVersionAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends <c>USERNAME</c> and <c>PASSWORD</c> when given (empty values are skipped).</summary>
    Task AuthenticateAsync(string? username, string? password, CancellationToken cancellationToken = default);

    /// <summary><c>LOGIN &lt;ups&gt;</c> - registers this client as attached to the UPS (upsmon secondary).</summary>
    Task LoginAsync(string upsName, CancellationToken cancellationToken = default);

    /// <summary><c>GET VAR &lt;ups&gt; &lt;var&gt;</c> - returns the unquoted value.</summary>
    Task<string> GetVarAsync(string upsName, string variable, CancellationToken cancellationToken = default);

    /// <summary><c>GET DESC &lt;ups&gt; &lt;var&gt;</c>.</summary>
    Task<string> GetVarDescriptionAsync(string upsName, string variable, CancellationToken cancellationToken = default);

    /// <summary><c>LIST VAR &lt;ups&gt;</c>.</summary>
    Task<IReadOnlyDictionary<string, string>> ListVarsAsync(string upsName, CancellationToken cancellationToken = default);

    /// <summary><c>LIST UPS</c>.</summary>
    Task<IReadOnlyList<NutUpsEntry>> ListUpsAsync(CancellationToken cancellationToken = default);

    /// <summary><c>LIST CMD &lt;ups&gt;</c>.</summary>
    Task<IReadOnlyList<string>> ListCommandsAsync(string upsName, CancellationToken cancellationToken = default);

    /// <summary>Sends <c>LOGOUT</c> if connected (errors ignored) and closes the connection. Idempotent.</summary>
    Task DisconnectAsync();
}
