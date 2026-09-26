using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Nut;

/// <summary>
/// Default <see cref="INutClient"/> implementation: one <see cref="TcpClient"/>/<see cref="NetworkStream"/>
/// connection to a NUT server (upsd), with requests serialized on a <see cref="SemaphoreSlim"/> so that
/// concurrent callers cannot interleave writes/reads on the same socket (the race the original WinNUT
/// client's <c>streamInUse</c> flag tried, and failed, to prevent).
/// </summary>
/// <remarks>
/// Wire format: ASCII/UTF-8 text lines terminated by <c>\n</c>. See
/// https://networkupstools.org/docs/developer-guide.chunked/net-protocol.html.
/// </remarks>
public sealed class NutClient : INutClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound for one protocol line; protects against a misbehaving server.</summary>
    internal const int MaxLineLength = 64 * 1024;

    private readonly ILogger<NutClient>? _logger;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private LineReader? _lineReader;
    private volatile bool _connected;
    private int _connectionLostRaised;
    private bool _disposed;

    /// <summary>Creates a client. <paramref name="timeout"/> defaults to 5 seconds when omitted.</summary>
    public NutClient(ILogger<NutClient>? logger = null, TimeSpan? timeout = null)
    {
        _logger = logger;
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Timeout must be positive.");
        }
    }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public event EventHandler<Exception?>? ConnectionLost;

    /// <inheritdoc />
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connected)
            {
                throw new InvalidOperationException("Already connected. Call DisconnectAsync first.");
            }

            _logger?.LogInformation("Connecting to NUT server {Host}:{Port}...", host, port);

            using var timeoutCts = new CancellationTokenSource(_timeout);
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var client = new TcpClient();
            bool connected = false;
            try
            {
                await client.ConnectAsync(host, port, linkedCts.Token).ConfigureAwait(false);
                connected = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                throw new NutTimeoutException($"Timed out connecting to {host}:{port}.", ex);
            }
            finally
            {
                if (!connected)
                {
                    client.Dispose();
                }
            }

            _client = client;
            _stream = client.GetStream();
            _lineReader = new LineReader(_stream);
            _connected = true;
            _connectionLostRaised = 0;

            _logger?.LogInformation("Connected to NUT server {Host}:{Port}.", host, port);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("VER", FreeTextReader("VER"), cancellationToken);

    /// <inheritdoc />
    public Task<string> GetProtocolVersionAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync("NETVER", FreeTextReader("NETVER"), cancellationToken);

    /// <inheritdoc />
    public async Task AuthenticateAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(username))
        {
            string command = $"USERNAME {NutResponseParser.QuoteArgument(username)}";
            await ExecuteAsync(command, ExpectOkReader(command), cancellationToken).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(password))
        {
            string command = $"PASSWORD {NutResponseParser.QuoteArgument(password)}";
            await ExecuteAsync(command, ExpectOkReader(command), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task LoginAsync(string upsName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);
        string command = $"LOGIN {NutResponseParser.QuoteArgument(upsName)}";
        return ExecuteAsync(command, ExpectOkReader(command), cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetVarAsync(string upsName, string variable, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);
        string command = $"GET VAR {NutResponseParser.QuoteArgument(upsName)} {NutResponseParser.QuoteArgument(variable)}";
        return ExecuteAsync(command, VarReader(command), cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> GetVarDescriptionAsync(string upsName, string variable, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);
        ArgumentException.ThrowIfNullOrWhiteSpace(variable);
        string command = $"GET DESC {NutResponseParser.QuoteArgument(upsName)} {NutResponseParser.QuoteArgument(variable)}";
        return ExecuteAsync(command, DescReader(command), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> ListVarsAsync(string upsName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);
        string command = $"LIST VAR {NutResponseParser.QuoteArgument(upsName)}";
        IReadOnlyList<string> lines = await ExecuteAsync(command, ListReader(command), cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            if (!NutResponseParser.TryParseVarLine(line, out string name, out string value))
            {
                throw UnrecognizedLine(command, line);
            }

            result[name] = value;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NutUpsEntry>> ListUpsAsync(CancellationToken cancellationToken = default)
    {
        const string command = "LIST UPS";
        IReadOnlyList<string> lines = await ExecuteAsync(command, ListReader(command), cancellationToken).ConfigureAwait(false);

        var result = new List<NutUpsEntry>(lines.Count);
        foreach (string line in lines)
        {
            if (!NutResponseParser.TryParseUpsLine(line, out string name, out string description))
            {
                throw UnrecognizedLine(command, line);
            }

            result.Add(new NutUpsEntry(name, description));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListCommandsAsync(string upsName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(upsName);
        string command = $"LIST CMD {NutResponseParser.QuoteArgument(upsName)}";
        IReadOnlyList<string> lines = await ExecuteAsync(command, ListReader(command), cancellationToken).ConfigureAwait(false);

        var result = new List<string>(lines.Count);
        foreach (string line in lines)
        {
            if (!NutResponseParser.TryParseCmdLine(line, out string name))
            {
                throw UnrecognizedLine(command, line);
            }

            result.Add(name);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_connected)
            {
                return;
            }

            _logger?.LogInformation("Disconnecting from NUT server.");

            TimeSpan logoutTimeout = _timeout < LogoutTimeout ? _timeout : LogoutTimeout;
            try
            {
                using var cts = new CancellationTokenSource(logoutTimeout);
                await WriteRawAsync("LOGOUT", cts.Token).ConfigureAwait(false);
                await ReadRawLineAsync(cts.Token).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Best-effort: the transport was already broken. LOGOUT is advisory only.
            }
            catch (SocketException)
            {
                // Best-effort, see above.
            }
            catch (OperationCanceledException)
            {
                // Best-effort: LOGOUT did not complete within the short timeout.
            }
            finally
            {
                CloseSocketCore();
            }

            _logger?.LogInformation("Disconnected from NUT server.");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    /// <summary>
    /// Sends <paramref name="command"/> and runs <paramref name="readResponseAsync"/> to consume the
    /// reply, all under the send lock and a single per-request timeout (<see cref="_timeout"/>) linked
    /// with <paramref name="cancellationToken"/>. Centralizes the client's error contract:
    /// <c>ERR</c> replies surface as <see cref="NutException"/> and leave the connection open (they are
    /// thrown by <paramref name="readResponseAsync"/> itself and simply propagate here); transport
    /// failures and our own timeout close the connection and raise <see cref="ConnectionLost"/> once;
    /// cancellation via the caller's own token propagates as <see cref="OperationCanceledException"/>
    /// and also closes the connection (its state is unknown after a cancelled I/O) without raising
    /// <see cref="ConnectionLost"/>. A timeout/cancellation that occurs only while queued for the send
    /// lock (nothing sent yet) leaves the connection untouched.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(
        string command,
        Func<CancellationToken, Task<T>> readResponseAsync,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_connected)
        {
            throw new InvalidOperationException("Not connected. Call ConnectAsync first.");
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _sendLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new NutTimeoutException($"Timed out waiting to send '{NutResponseParser.MaskCommand(command)}'.");
        }

        // ConnectionLost is raised only after the lock is released, so handlers may call back into the client.
        Exception? connectionLostError = null;
        try
        {
            if (!_connected)
            {
                throw new IOException("The NUT connection is no longer available.");
            }

            await WriteRawAsync(command, linkedCts.Token).ConfigureAwait(false);
            return await readResponseAsync(linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The connection's state is unknown after a cancelled write/read; close it, but this was
            // requested by the caller, not an unexpected break, so ConnectionLost does not fire.
            CloseConnection(markLost: false);
            throw;
        }
        catch (OperationCanceledException)
        {
            var timeoutEx = new NutTimeoutException(
                $"Timed out waiting for a response to '{NutResponseParser.MaskCommand(command)}'.");
            if (CloseConnection(markLost: true))
            {
                connectionLostError = timeoutEx;
            }

            throw timeoutEx;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            IOException io = ex as IOException
                ?? new IOException($"NUT request '{NutResponseParser.MaskCommand(command)}' failed: {ex.Message}", ex);
            if (CloseConnection(markLost: true))
            {
                connectionLostError = io;
            }

            throw io;
        }
        finally
        {
            _sendLock.Release();
            if (connectionLostError is not null)
            {
                RaiseConnectionLost(connectionLostError);
            }
        }
    }

    private Func<CancellationToken, Task<string>> FreeTextReader(string command) =>
        async ct =>
        {
            string line = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
            if (line.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw NutResponseParser.CreateError(command, line);
            }

            return line;
        };

    private Func<CancellationToken, Task<string>> ExpectOkReader(string command) =>
        async ct =>
        {
            string line = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
            if (line.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw NutResponseParser.CreateError(command, line);
            }

            if (!line.StartsWith("OK", StringComparison.Ordinal))
            {
                throw UnrecognizedLine(command, line);
            }

            return line;
        };

    private Func<CancellationToken, Task<string>> VarReader(string command) =>
        async ct =>
        {
            string line = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
            if (line.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw NutResponseParser.CreateError(command, line);
            }

            if (!NutResponseParser.TryParseVarLine(line, out _, out string value))
            {
                throw UnrecognizedLine(command, line);
            }

            return value;
        };

    private Func<CancellationToken, Task<string>> DescReader(string command) =>
        async ct =>
        {
            string line = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
            if (line.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw NutResponseParser.CreateError(command, line);
            }

            if (!NutResponseParser.TryParseDescLine(line, out _, out string description))
            {
                throw UnrecognizedLine(command, line);
            }

            return description;
        };

    private Func<CancellationToken, Task<IReadOnlyList<string>>> ListReader(string command) =>
        async ct =>
        {
            string first = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
            if (first.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw NutResponseParser.CreateError(command, first);
            }

            if (!first.StartsWith("BEGIN", StringComparison.Ordinal))
            {
                throw UnrecognizedLine(command, first);
            }

            var lines = new List<string>();
            while (true)
            {
                string line = await ReadLineOrThrowAsync(ct).ConfigureAwait(false);
                if (line.StartsWith("END", StringComparison.Ordinal))
                {
                    break;
                }

                lines.Add(line);
            }

            return lines;
        };

    /// <summary>Reads one line, turning an end-of-stream (server closed the connection) into an <see cref="IOException"/>.</summary>
    private async Task<string> ReadLineOrThrowAsync(CancellationToken cancellationToken)
    {
        string? line = await ReadRawLineAsync(cancellationToken).ConfigureAwait(false);
        return line ?? throw new IOException("The NUT server closed the connection.");
    }

    private static NutException UnrecognizedLine(string command, string line) =>
        new(NutErrorCode.Unrecognized, NutResponseParser.MaskCommand(command), line);

    private async Task WriteRawAsync(string command, CancellationToken cancellationToken)
    {
        _logger?.LogDebug("NUT >> {Command}", NutResponseParser.MaskCommand(command));
        byte[] bytes = Encoding.UTF8.GetBytes(command + "\n");
        await _stream!.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ReadRawLineAsync(CancellationToken cancellationToken)
    {
        string? line = await _lineReader!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogDebug("NUT << {Line}", line ?? "<eof>");
        return line;
    }

    /// <summary>
    /// Closes the socket. Returns <see langword="true"/> when <paramref name="markLost"/> is set and this is the
    /// first unexpected loss of the current connection, i.e. the caller must raise <see cref="ConnectionLost"/>.
    /// </summary>
    private bool CloseConnection(bool markLost)
    {
        bool wasConnected = _connected;
        CloseSocketCore();
        return markLost && wasConnected && Interlocked.Exchange(ref _connectionLostRaised, 1) == 0;
    }

    private void RaiseConnectionLost(Exception error)
    {
        _logger?.LogInformation(error, "NUT connection lost.");
        try
        {
            ConnectionLost?.Invoke(this, error);
        }
#pragma warning disable CA1031 // A faulty subscriber must not replace the transport error thrown to the caller.
        catch (Exception handlerEx)
#pragma warning restore CA1031
        {
            _logger?.LogError(handlerEx, "A ConnectionLost handler threw an exception.");
        }
    }

    private void CloseSocketCore()
    {
        _connected = false;
        _lineReader = null;
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
    }

    /// <summary>
    /// Minimal buffered async line reader over a <see cref="Stream"/>. Splits on <c>\n</c> and trims a
    /// trailing <c>\r</c>; returns <see langword="null"/> on end of stream (never a partial last line).
    /// Written by hand (rather than <see cref="StreamReader"/>) so cancellation reliably reaches the
    /// underlying <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/> call.
    /// </summary>
    private sealed class LineReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[4096];
        private int _length;
        private int _offset;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            List<byte>? overflow = null;

            while (true)
            {
                if (_offset >= _length)
                {
                    _length = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                    _offset = 0;
                    if (_length == 0)
                    {
                        return null;
                    }
                }

                int newlineIndex = Array.IndexOf(_buffer, (byte)'\n', _offset, _length - _offset);
                if (newlineIndex < 0)
                {
                    if ((overflow?.Count ?? 0) + (_length - _offset) > MaxLineLength)
                    {
                        throw new IOException($"The NUT server sent a line longer than {MaxLineLength} bytes.");
                    }

                    overflow ??= new List<byte>();
                    overflow.AddRange(_buffer.AsSpan(_offset, _length - _offset).ToArray());
                    _offset = _length;
                    continue;
                }

                ReadOnlySpan<byte> chunk = _buffer.AsSpan(_offset, newlineIndex - _offset);
                _offset = newlineIndex + 1;

                if ((overflow?.Count ?? 0) + chunk.Length > MaxLineLength)
                {
                    throw new IOException($"The NUT server sent a line longer than {MaxLineLength} bytes.");
                }

                if (overflow is null)
                {
                    return DecodeTrimCr(chunk);
                }

                overflow.AddRange(chunk.ToArray());
                return DecodeTrimCr(overflow.ToArray());
            }
        }

        private static string DecodeTrimCr(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > 0 && bytes[^1] == (byte)'\r')
            {
                bytes = bytes[..^1];
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }
}
