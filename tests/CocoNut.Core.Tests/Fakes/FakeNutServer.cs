using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CocoNut.Core.Tests.Fakes;

/// <summary>
/// A minimal, scriptable stand-in for a NUT server (upsd) for protocol-level tests. Binds a
/// <see cref="TcpListener"/> to 127.0.0.1 on an OS-assigned port (<see cref="Port"/>) and accepts client
/// connections in the background until disposed.
/// </summary>
/// <remarks>
/// <para>
/// Behaviour is entirely driven by the handler set with <see cref="SetHandler"/>: it is called once per
/// received request line and returns the response line(s) to send back (an empty sequence sends
/// nothing back at all - handy for simulating a server that never answers, e.g. for client timeout
/// tests). The handler runs on the connection's read loop, so it may safely call back into this
/// instance, e.g. <see cref="DropConnection"/>, to simulate the server disconnecting while handling one
/// particular request.
/// </para>
/// <para>
/// This type is intended to be reused across NUT work packages' tests, not just the client's own -
/// keep it protocol-agnostic (it just relays lines) rather than growing NUT-specific behaviour here.
/// </para>
/// <example>
/// <code>
/// await using var server = new FakeNutServer();
/// await server.StartAsync();
/// server.SetHandler(request => request switch
/// {
///     "VER" => ["Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/"],
///     "GET VAR ups1 battery.charge" => ["VAR ups1 battery.charge \"100\""],
///     _ => ["ERR UNKNOWN-COMMAND"],
/// });
///
/// await using var client = new NutClient();
/// await client.ConnectAsync("127.0.0.1", server.Port);
/// </code>
/// </example>
/// </remarks>
public sealed class FakeNutServer : IAsyncDisposable
{
    // Encoding.UTF8 emits a byte-order-mark preamble on a StreamWriter's first write, which would
    // otherwise corrupt the first response line of every connection ("﻿OK" instead of "OK").
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TcpListener _listener;
    private readonly List<string> _receivedLines = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();

    private Func<string, IEnumerable<string>>? _handler;
    private TimeSpan _responseDelay = TimeSpan.Zero;
    private TcpClient? _connectedClient;
    private Task? _acceptLoopTask;
    private bool _disposed;

    /// <summary>Creates a server; call <see cref="StartAsync"/> before connecting to it.</summary>
    public FakeNutServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
    }

    /// <summary>The OS-assigned TCP port <see cref="StartAsync"/> bound to on 127.0.0.1.</summary>
    public int Port { get; private set; }

    /// <summary>A snapshot of every request line received so far, across all connections, in order.</summary>
    public IReadOnlyList<string> ReceivedLines
    {
        get
        {
            lock (_lock)
            {
                return [.. _receivedLines];
            }
        }
    }

    /// <summary>
    /// Sets the handler mapping one received request line to the response line(s) to send back.
    /// Replacing the handler takes effect for the next request that is received; it is safe to call
    /// this from the test body while a connection is already open.
    /// </summary>
    public void SetHandler(Func<string, IEnumerable<string>> handler) => _handler = handler;

    /// <summary>
    /// Sets a delay applied after receiving a request and before writing back its response(s).
    /// Useful for exercising client-side timeouts and cancellation without a real slow network.
    /// </summary>
    public void SetResponseDelay(TimeSpan delay) => _responseDelay = delay;

    /// <summary>Starts listening. Safe to call only once per instance.</summary>
    public Task StartAsync()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoopTask = AcceptLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Forcibly closes the most recently accepted client connection, simulating the server dropping the
    /// link. Any pending or subsequent read/write by the client on that connection will fail.
    /// </summary>
    public void DropConnection()
    {
        lock (_lock)
        {
            _connectedClient?.Close();
            _connectedClient = null;
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

        await _cts.CancelAsync().ConfigureAwait(false);
        _listener.Stop();
        DropConnection();

        if (_acceptLoopTask is not null)
        {
            await _acceptLoopTask.ConfigureAwait(false);
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                return;
            }

            lock (_lock)
            {
                _connectedClient = client;
            }

            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            using NetworkStream stream = client.GetStream();
            using var reader = new StreamReader(stream, NoBomUtf8);
            using var writer = new StreamWriter(stream, NoBomUtf8) { NewLine = "\n", AutoFlush = true };

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                lock (_lock)
                {
                    _receivedLines.Add(line);
                }

                IEnumerable<string> responses = _handler?.Invoke(line) ?? [];

                if (_responseDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_responseDelay, cancellationToken).ConfigureAwait(false);
                }

                foreach (string response in responses)
                {
                    await writer.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (IOException)
        {
            // The client disconnected (or we did, via DropConnection) mid read/write; nothing to do.
        }
        catch (SocketException)
        {
            // As above.
        }
        catch (OperationCanceledException)
        {
            // Server is shutting down.
        }
        finally
        {
            client.Dispose();
        }
    }
}
