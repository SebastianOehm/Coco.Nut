using System.Net.Sockets;
using CocoNut.Core.Nut;

namespace CocoNut.Core.Tests.Fakes;

/// <summary>
/// A scriptable <see cref="INutClient"/> test double: in-memory variables plus a queue of results per method so a
/// test can make the Nth call to any method fail with a specific <see cref="NutException"/> or transport error,
/// then let subsequent calls succeed. Every method also increments a public call counter.
/// </summary>
/// <remarks>
/// A queue holding <see langword="null"/> (or being empty) means "succeed". Enqueuing an <see cref="IOException"/>
/// or <see cref="SocketException"/> also flips <see cref="IsConnected"/> to <see langword="false"/> and raises
/// <see cref="ConnectionLost"/> once, per the <see cref="INutClient"/> error contract, before the exception is
/// thrown back to the caller.
/// </remarks>
public sealed class FakeNutClient : INutClient
{
    public Dictionary<string, string> Variables { get; } = new();

    public Dictionary<string, string> Descriptions { get; } = new();

    public List<NutUpsEntry> UpsEntries { get; } = new();

    public List<string> UpsCommands { get; } = new();

    public string ServerVersionValue { get; set; } = "Network UPS Tools upsd 2.8.1 - https://www.networkupstools.org/";

    public string ProtocolVersionValue { get; set; } = "1.3";

    public Queue<Exception?> ConnectResults { get; } = new();

    public Queue<Exception?> AuthenticateResults { get; } = new();

    public Queue<Exception?> LoginResults { get; } = new();

    public Queue<Exception?> ListVarsResults { get; } = new();

    public Queue<Exception?> GetServerVersionResults { get; } = new();

    public Queue<Exception?> GetProtocolVersionResults { get; } = new();

    public int ConnectCalls { get; private set; }

    public int AuthenticateCalls { get; private set; }

    public int LoginCalls { get; private set; }

    public int ListVarsCalls { get; private set; }

    public int GetVarCalls { get; private set; }

    public int GetVarDescriptionCalls { get; private set; }

    public int ListUpsCalls { get; private set; }

    public int ListCommandsCalls { get; private set; }

    public int GetServerVersionCalls { get; private set; }

    public int GetProtocolVersionCalls { get; private set; }

    public int DisconnectCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public int ConnectionLostRaisedCount { get; private set; }

    public bool IsConnected { get; private set; }

    public event EventHandler<Exception?>? ConnectionLost;

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        var error = Dequeue(ConnectResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task<string> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        GetServerVersionCalls++;
        var error = Dequeue(GetServerVersionResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        return Task.FromResult(ServerVersionValue);
    }

    public Task<string> GetProtocolVersionAsync(CancellationToken cancellationToken = default)
    {
        GetProtocolVersionCalls++;
        var error = Dequeue(GetProtocolVersionResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        return Task.FromResult(ProtocolVersionValue);
    }

    public Task AuthenticateAsync(string? username, string? password, CancellationToken cancellationToken = default)
    {
        AuthenticateCalls++;
        var error = Dequeue(AuthenticateResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        return Task.CompletedTask;
    }

    public Task LoginAsync(string upsName, CancellationToken cancellationToken = default)
    {
        LoginCalls++;
        var error = Dequeue(LoginResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        return Task.CompletedTask;
    }

    public Task<string> GetVarAsync(string upsName, string variable, CancellationToken cancellationToken = default)
    {
        GetVarCalls++;
        if (Variables.TryGetValue(variable, out var value))
        {
            return Task.FromResult(value);
        }

        throw new NutException(NutErrorCode.VarNotSupported, $"GET VAR {upsName} {variable}", "ERR VAR-NOT-SUPPORTED");
    }

    public Task<string> GetVarDescriptionAsync(string upsName, string variable, CancellationToken cancellationToken = default)
    {
        GetVarDescriptionCalls++;
        if (Descriptions.TryGetValue(variable, out var value))
        {
            return Task.FromResult(value);
        }

        throw new NutException(NutErrorCode.VarNotSupported, $"GET DESC {upsName} {variable}", "ERR VAR-NOT-SUPPORTED");
    }

    public Task<IReadOnlyDictionary<string, string>> ListVarsAsync(string upsName, CancellationToken cancellationToken = default)
    {
        ListVarsCalls++;
        var error = Dequeue(ListVarsResults);
        if (error is not null)
        {
            HandleFailure(error);
            throw error;
        }

        // Snapshot so a test can mutate Variables between polls to simulate a changing UPS.
        return Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Variables));
    }

    public Task<IReadOnlyList<NutUpsEntry>> ListUpsAsync(CancellationToken cancellationToken = default)
    {
        ListUpsCalls++;
        return Task.FromResult<IReadOnlyList<NutUpsEntry>>(new List<NutUpsEntry>(UpsEntries));
    }

    public Task<IReadOnlyList<string>> ListCommandsAsync(string upsName, CancellationToken cancellationToken = default)
    {
        ListCommandsCalls++;
        return Task.FromResult<IReadOnlyList<string>>(new List<string>(UpsCommands));
    }

    public Task DisconnectAsync()
    {
        DisconnectCalls++;
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }

    /// <summary>Raises <see cref="ConnectionLost"/> as a real client would when its socket breaks on its own,
    /// between calls (not as a direct result of one of this fake's methods throwing).</summary>
    public void SimulateConnectionLost(Exception? error = null)
    {
        HandleFailure(error ?? new IOException("Simulated connection loss."));
    }

    private void HandleFailure(Exception? error)
    {
        if (error is IOException or SocketException)
        {
            IsConnected = false;
            ConnectionLostRaisedCount++;
            ConnectionLost?.Invoke(this, error);
        }
    }

    private static Exception? Dequeue(Queue<Exception?> queue) => queue.Count > 0 ? queue.Dequeue() : null;
}
