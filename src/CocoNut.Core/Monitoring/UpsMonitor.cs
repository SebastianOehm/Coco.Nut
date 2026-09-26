using System.Net.Sockets;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Monitoring;

/// <summary>
/// Connects to a NUT server for one UPS, polls it and raises events for the UI. Ports the connect/login/poll/
/// reconnect state machine of WinNUT's <c>UPS_Device.vb</c> onto an async <see cref="INutClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// Differences from WinNUT, intentional: (1) product info, the power method and <c>battery.capacity</c> are read
/// from a single <c>LIST VAR</c> response instead of many individual <c>GET VAR</c> calls (WinNUT's
/// <c>GetUPSProductInfo</c>); this also means they self-heal from a transient error on the first successful poll
/// instead of being permanently unavailable for the connection's lifetime. (2) WinNUT's reconnect timer retries
/// forever at a fixed 5 second interval; this monitor also retries forever (while <see cref="ConnectionSettings.AutoReconnect"/>
/// is set) but with a delay that doubles from 5 seconds up to a 30 second cap, to avoid hammering a server that is
/// down for a while. (3) A failing login (bad credentials, unknown UPS, ...) never triggers a reconnect in either
/// implementation, because retrying cannot fix it.
/// </para>
/// <para>
/// Thread safety: <see cref="StartAsync"/>, <see cref="StopAsync"/> and <see cref="RestartAsync"/> are serialized
/// against each other. Events are raised from the internal poll/reconnect loop (a thread-pool continuation, per
/// WinNUT's WinForms timer callbacks); no event is raised once <see cref="StopAsync"/> has returned, and an
/// exception thrown by an event handler is caught and logged so it cannot stop the loop.
/// </para>
/// </remarks>
public sealed class UpsMonitor : IAsyncDisposable
{
    private const int MinPollIntervalMs = 250;
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinResumeGapThreshold = TimeSpan.FromSeconds(30);
    private const int MaxDataStaleRetries = 3; // WinNUT: UPS_Device.MAX_VAR_RETRIES

    private readonly Func<INutClient> _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpsMonitor> _logger;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    private INutClient? _client;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private double _nominalFrequency;
    private double? _batteryCapacity;
    private UpsStatus _lastStatus;
    private Exception? _lastError;
    private bool _disposed;

    /// <summary>Creates a monitor. Nothing is connected until <see cref="StartAsync"/> is called.</summary>
    /// <param name="clientFactory">Creates a fresh <see cref="INutClient"/> for each connection attempt (initial
    /// connect and every reconnect), so a client with broken internal socket state is never reused.</param>
    /// <param name="timeProvider">Used for all timing (poll interval, reconnect backoff, resume detection) so tests
    /// can drive the monitor deterministically with a fake clock.</param>
    /// <param name="logger">Receives tracing that mirrors WinNUT's <c>LogFile.LogTracing</c> calls.</param>
    public UpsMonitor(Func<INutClient> clientFactory, TimeProvider timeProvider, ILogger<UpsMonitor> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Current lifecycle state.</summary>
    public MonitorState State { get; private set; } = MonitorState.Disconnected;

    /// <summary>Static product info, available once the first <c>LIST VAR</c> after login has succeeded.</summary>
    public UpsInfo? Info { get; private set; }

    /// <summary>The power calculation method selected for the current connection.</summary>
    public PowerMethod PowerMethod { get; private set; } = PowerMethod.Unavailable;

    /// <summary>The most recent successful poll result.</summary>
    public UpsReading? LastReading { get; private set; }

    /// <summary><c>VER</c> response, or <see langword="null"/> when not yet read or unavailable.</summary>
    public string? ServerVersion { get; private set; }

    /// <summary><c>NETVER</c> response, or <see langword="null"/> when not yet read or unavailable.</summary>
    public string? ProtocolVersion { get; private set; }

    /// <summary>The connection settings passed to the most recent <see cref="StartAsync"/> call.</summary>
    public ConnectionSettings? Settings { get; private set; }

    /// <summary>Raised on every state transition.</summary>
    public event EventHandler<MonitorStateChangedEventArgs>? StateChanged;

    /// <summary>Raised after every successful poll.</summary>
    public event EventHandler<UpsReading>? ReadingUpdated;

    /// <summary>
    /// Raised when a poll's <see cref="UpsStatus"/> differs from the previous one. WinNUT only raised this for
    /// newly active flags; see <see cref="UpsStatusChangedEventArgs"/> for both directions.
    /// </summary>
    public event EventHandler<UpsStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Raised once when an established connection breaks unexpectedly, before any reconnect attempt (WinNUT:
    /// <c>UPS_Device.Lost_Connect</c> / <c>Socket_Broken</c>). Not raised for a failed initial connection attempt
    /// (see <see cref="StateChanged"/> instead) nor for the deliberate reconnect forced by a detected clock gap.
    /// </summary>
    public event EventHandler<Exception?>? ConnectionLost;

    /// <summary>Raised before each reconnect attempt, starting at 1.</summary>
    public event EventHandler<int>? ReconnectAttempt;

    /// <summary>
    /// Connects, authenticates, logs in, reads product info and starts polling. Ports <c>UPS_Device.Connect_UPS</c>
    /// + WinNUT's <c>WinNUT.UPS_Connect</c>. Returns once the first connection attempt has concluded (successfully,
    /// fatally, or by having handed off to the background reconnect loop); the poll loop itself always runs in the
    /// background. Failures are reported through <see cref="StateChanged"/>, not by throwing - see the type remarks.
    /// </summary>
    /// <param name="connection">Host/port/credentials/UPS name/poll interval/auto-reconnect.</param>
    /// <param name="nominalFrequency">Fallback frequency (WinNUT: <c>CAL_FreqInNom</c>) used only when the UPS
    /// reports no frequency variable at all.</param>
    /// <param name="cancellationToken">Cancels the initial connection attempt only (not the background loop once
    /// started; use <see cref="StopAsync"/> for that).</param>
    public async Task StartAsync(ConnectionSettings connection, double nominalFrequency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopRunningLoopAsync().ConfigureAwait(false);
            await StartLoopAsync(connection, nominalFrequency, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Stops polling, logs out (WinNUT: <c>UPS_Device.Disconnect</c>) and transitions to
    /// <see cref="MonitorState.Disconnected"/>. Idempotent; safe to call when never started. No event is raised
    /// after this method returns.
    /// </summary>
    public async Task StopAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopCoreAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Stops and starts again with the settings and nominal frequency from the last <see cref="StartAsync"/> call.
    /// Used after resume-from-sleep (in addition to the monitor's own gap detection) and after a settings change.
    /// </summary>
    public async Task RestartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var settings = Settings ?? throw new InvalidOperationException($"{nameof(RestartAsync)} requires a previous {nameof(StartAsync)} call.");
            var nominalFrequency = _nominalFrequency;
            await StopRunningLoopAsync().ConfigureAwait(false);
            await StartLoopAsync(settings, nominalFrequency, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// Reads every UPS variable (WinNUT: <c>UPS_Device.GetUPS_ListVar</c>), optionally with its <c>GET DESC</c>
    /// description, for the variables window. A missing description for one variable is tolerated and leaves
    /// <see cref="NutVariable.Description"/> null for that entry.
    /// </summary>
    public async Task<IReadOnlyList<NutVariable>> GetAllVariablesAsync(bool includeDescriptions, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = _client;
        var settings = Settings;
        if (client is null || settings is null)
        {
            throw new InvalidOperationException("The UPS monitor is not connected.");
        }

        var vars = await client.ListVarsAsync(settings.UpsName, cancellationToken).ConfigureAwait(false);
        var result = new List<NutVariable>(vars.Count);
        foreach (var (name, value) in vars)
        {
            string? description = null;
            if (includeDescriptions)
            {
                try
                {
                    description = await client.GetVarDescriptionAsync(settings.UpsName, name, cancellationToken).ConfigureAwait(false);
                }
                catch (NutException ex)
                {
                    _logger.LogDebug(ex, "No description available for UPS variable {Variable}.", name);
                }
            }

            result.Add(new NutVariable(name, value, description));
        }

        return result;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopCoreAsync().ConfigureAwait(false);
        _lifecycleGate.Dispose();
    }

    // ---- Lifecycle plumbing --------------------------------------------------------------------------------------

    private async Task StopCoreAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopRunningLoopAsync().ConfigureAwait(false);
            SetState(MonitorState.Disconnected, null);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    // ---- Lifecycle plumbing (must be called while holding _lifecycleGate) --------------------------------------

    private async Task StopRunningLoopAsync()
    {
        _runCts?.Cancel();
        if (_runTask is not null)
        {
            try
            {
                await _runTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UPS monitor loop ended with an exception during stop/restart.");
            }
        }

        await DisconnectClientAsync().ConfigureAwait(false);
        _runCts?.Dispose();
        _runCts = null;
        _runTask = null;
    }

    private async Task StartLoopAsync(ConnectionSettings connection, double nominalFrequency, CancellationToken cancellationToken)
    {
        Settings = connection;
        _nominalFrequency = nominalFrequency;
        _batteryCapacity = null;
        _lastStatus = UpsStatus.None;
        LastReading = null;
        Info = null;
        PowerMethod = PowerMethod.Unavailable;
        ServerVersion = null;
        ProtocolVersion = null;
        _lastError = null;

        SetState(MonitorState.Connecting, null);
        var outcome = await ConnectOnceAsync(cancellationToken).ConfigureAwait(false);

        switch (outcome)
        {
            case ConnectOutcome.Connected:
                SetState(MonitorState.Connected, null);
                _runCts = new CancellationTokenSource();
                _runTask = RunLoopAsync(startConnected: true, _runCts.Token);
                break;

            case ConnectOutcome.FatalStop:
                SetState(MonitorState.Disconnected, _lastError);
                break;

            case ConnectOutcome.TransportFailure:
                if (connection.AutoReconnect)
                {
                    SetState(MonitorState.Reconnecting, _lastError);
                    _runCts = new CancellationTokenSource();
                    _runTask = RunLoopAsync(startConnected: false, _runCts.Token);
                }
                else
                {
                    SetState(MonitorState.Disconnected, _lastError);
                }

                break;
        }
    }

    // ---- Background loop -----------------------------------------------------------------------------------------

    private enum ConnectOutcome
    {
        Connected,
        FatalStop,
        TransportFailure,
    }

    private enum PollOutcome
    {
        Success,
        Skipped,
        TransportFailure,
        Cancelled,
    }

    private enum PollExitReason
    {
        Cancelled,
        ReconnectRequested,
        StopRequested,
    }

    private async Task RunLoopAsync(bool startConnected, CancellationToken ct)
    {
        try
        {
            var connected = startConnected;
            while (true)
            {
                if (!connected)
                {
                    connected = await ReconnectLoopAsync(ct).ConfigureAwait(false);
                    if (!connected)
                    {
                        return;
                    }
                }

                var reason = await PollUntilDisconnectedAsync(ct).ConfigureAwait(false);
                if (reason != PollExitReason.ReconnectRequested)
                {
                    return;
                }

                connected = false;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync/RestartAsync.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in the UPS monitor loop.");
            SetState(MonitorState.Disconnected, ex);
        }
    }

    private async Task<bool> ReconnectLoopAsync(CancellationToken ct)
    {
        var delay = InitialReconnectDelay;
        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            attempt++;
            Raise(ReconnectAttempt, attempt);
            _logger.LogInformation("Reconnect attempt {Attempt} to {Host}:{Port}.", attempt, Settings!.Host, Settings.Port);

            var outcome = await ConnectOnceAsync(ct).ConfigureAwait(false);
            switch (outcome)
            {
                case ConnectOutcome.Connected:
                    SetState(MonitorState.Connected, null);
                    return true;

                case ConnectOutcome.FatalStop:
                    SetState(MonitorState.Disconnected, _lastError);
                    return false;

                case ConnectOutcome.TransportFailure:
                    try
                    {
                        await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }

                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, MaxReconnectDelay.TotalSeconds));
                    break;
            }
        }
    }

    private async Task<PollExitReason> PollUntilDisconnectedAsync(CancellationToken ct)
    {
        var settings = Settings!;
        var interval = TimeSpan.FromMilliseconds(Math.Max(settings.PollIntervalMs, MinPollIntervalMs));
        var gapThreshold = Max(interval * 3, MinResumeGapThreshold);

        using var timer = new PeriodicTimer(interval, _timeProvider);

        // WinNUT: "Have UPS data available right away" - Connect_UPS calls Retrieve_UPS_Datas once before starting
        // the polling timer, instead of waiting a full interval for the first reading.
        var firstOutcome = await SafePollOnceAsync(ct).ConfigureAwait(false);
        switch (firstOutcome)
        {
            case PollOutcome.TransportFailure:
                return await ExitPollingAsync(_lastError, forcedByGap: false).ConfigureAwait(false);
            case PollOutcome.Cancelled:
                return PollExitReason.Cancelled;
        }

        var lastPollAt = _timeProvider.GetUtcNow();

        while (true)
        {
            bool ticked;
            try
            {
                ticked = await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return PollExitReason.Cancelled;
            }

            if (!ticked)
            {
                return PollExitReason.Cancelled;
            }

            var now = _timeProvider.GetUtcNow();
            if (now - lastPollAt > gapThreshold)
            {
                _logger.LogWarning(
                    "Detected a {Gap} gap since the last poll (system likely suspended and resumed); forcing a reconnect.",
                    now - lastPollAt);
                lastPollAt = now;
                return await ExitPollingAsync(null, forcedByGap: true).ConfigureAwait(false);
            }

            lastPollAt = now;

            var outcome = await SafePollOnceAsync(ct).ConfigureAwait(false);
            switch (outcome)
            {
                case PollOutcome.TransportFailure:
                    return await ExitPollingAsync(_lastError, forcedByGap: false).ConfigureAwait(false);
                case PollOutcome.Cancelled:
                    return PollExitReason.Cancelled;
            }
        }
    }

    private async Task<PollExitReason> ExitPollingAsync(Exception? error, bool forcedByGap)
    {
        await DisconnectClientAsync().ConfigureAwait(false);

        if (!forcedByGap)
        {
            Raise(ConnectionLost, error);
        }

        if (Settings!.AutoReconnect)
        {
            SetState(MonitorState.Reconnecting, error);
            return PollExitReason.ReconnectRequested;
        }

        SetState(MonitorState.Disconnected, error);
        return PollExitReason.StopRequested;
    }

    private async Task<PollOutcome> SafePollOnceAsync(CancellationToken ct)
    {
        try
        {
            return await PollOnceAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PollOutcome.Cancelled;
        }
    }

    private async Task<PollOutcome> PollOnceAsync(CancellationToken ct)
    {
        var client = _client;
        var settings = Settings;
        if (client is null || settings is null)
        {
            return PollOutcome.Skipped;
        }

        IReadOnlyDictionary<string, string>? vars = null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                vars = await client.ListVarsAsync(settings.UpsName, ct).ConfigureAwait(false);
                break;
            }
            catch (NutException ex) when (ex.ErrorCode == NutErrorCode.DataStale)
            {
                // WinNUT: UPS_Device.GetUPSVar retries DATA-STALE up to MAX_VAR_RETRIES times before giving up.
                if (attempt >= MaxDataStaleRetries)
                {
                    _logger.LogError("DATA-STALE persisted for {Ups} after {Retries} retries; skipping this poll.", settings.UpsName, MaxDataStaleRetries);
                    return PollOutcome.Skipped;
                }

                _logger.LogWarning("DATA-STALE for {Ups}, retry {Attempt}/{Max}.", settings.UpsName, attempt + 1, MaxDataStaleRetries);
            }
            catch (NutException ex) when (ex.ErrorCode == NutErrorCode.DriverNotConnected)
            {
                _logger.LogWarning(ex, "Driver not connected for {Ups}; skipping this poll.", settings.UpsName);
                return PollOutcome.Skipped;
            }
            catch (NutException ex)
            {
                _logger.LogWarning(ex, "Unexpected NUT error while polling {Ups}; skipping this poll.", settings.UpsName);
                return PollOutcome.Skipped;
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                _logger.LogWarning(ex, "Connection lost while polling {Ups}.", settings.UpsName);
                _lastError = ex;
                return PollOutcome.TransportFailure;
            }
        }

        try
        {
            if (Info is null)
            {
                // Self-heals a transient failure of the initial LIST VAR done in ConnectOnceAsync (see the type
                // remarks): once any poll succeeds, product info/power method/battery capacity are derived from it.
                Info = UpsCalculations.BuildInfo(vars);
                PowerMethod = UpsCalculations.SelectPowerMethod(vars);
                _batteryCapacity = ParseDoubleOrNull(vars, "battery.capacity");
            }

            UpsStatusParser.Parse(vars.GetValueOrDefault("ups.status"), out var unknownTokens);
            if (unknownTokens.Count > 0)
            {
                _logger.LogWarning("Unknown ups.status token(s) ignored: {Tokens}", string.Join(", ", unknownTokens));
            }

            var reading = UpsCalculations.BuildReading(vars, PowerMethod, _batteryCapacity, _nominalFrequency, _timeProvider.GetUtcNow());
            LastReading = reading;
            Raise(ReadingUpdated, reading);

            var previous = _lastStatus;
            if (reading.Status != previous)
            {
                _lastStatus = reading.Status;
                var newlyActive = reading.Status & ~previous;
                var newlyCleared = previous & ~reading.Status;
                Raise(StatusChanged, new UpsStatusChangedEventArgs(previous, reading.Status, newlyActive, newlyCleared));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build a UPS reading from the polled variables; skipping this poll.");
            return PollOutcome.Skipped;
        }

        return PollOutcome.Success;
    }

    private async Task<ConnectOutcome> ConnectOnceAsync(CancellationToken ct)
    {
        var settings = Settings!;
        var client = _clientFactory();

        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            _logger.LogWarning(ex, "Failed to connect to NUT server {Host}:{Port}.", settings.Host, settings.Port);
            _lastError = ex;
            await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            return ConnectOutcome.TransportFailure;
        }

        // WinNUT logs VER/NETVER failures but does not treat them as fatal.
        try
        {
            ServerVersion = await client.GetServerVersionAsync(ct).ConfigureAwait(false);
        }
        catch (NutException ex)
        {
            _logger.LogWarning(ex, "VER failed.");
        }

        try
        {
            ProtocolVersion = await client.GetProtocolVersionAsync(ct).ConfigureAwait(false);
        }
        catch (NutException ex)
        {
            _logger.LogWarning(ex, "NETVER failed.");
        }

        try
        {
            await client.AuthenticateAsync(settings.Username, settings.Password, ct).ConfigureAwait(false);
            await client.LoginAsync(settings.UpsName, ct).ConfigureAwait(false);
        }
        catch (NutException ex)
        {
            // A rejected login (bad credentials, unknown UPS, ...) cannot be fixed by retrying.
            _logger.LogError(ex, "Login to {Ups} on {Host}:{Port} failed with {Code}.", settings.UpsName, settings.Host, settings.Port, ex.ErrorCode);
            _lastError = ex;
            await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            return ConnectOutcome.FatalStop;
        }
        catch (OperationCanceledException)
        {
            await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            _logger.LogWarning(ex, "Connection lost while authenticating/logging in to {Host}:{Port}.", settings.Host, settings.Port);
            _lastError = ex;
            await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
            return ConnectOutcome.TransportFailure;
        }

        _client = client;
        _lastError = null;
        return ConnectOutcome.Connected;
    }

    private async Task DisconnectClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        try
        {
            await client.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while disconnecting from the NUT server.");
        }

        await DisposeClientQuietlyAsync(client).ConfigureAwait(false);
    }

    private async Task DisposeClientQuietlyAsync(INutClient client)
    {
        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while disposing the NUT client.");
        }
    }

    // ---- Small helpers --------------------------------------------------------------------------------------------

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static double? ParseDoubleOrNull(IReadOnlyDictionary<string, string> vars, string key) =>
        vars.TryGetValue(key, out var raw) &&
        double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private void SetState(MonitorState newState, Exception? error)
    {
        var old = State;
        State = newState;
        Raise(StateChanged, new MonitorStateChangedEventArgs(old, newState, error));
    }

    /// <summary>
    /// Raises an event with each subscriber invoked independently: one handler throwing is logged and does not
    /// prevent the remaining handlers from running or stop the monitor's own loop.
    /// </summary>
    private void Raise<TArgs>(EventHandler<TArgs>? handler, TArgs args)
    {
        if (handler is null)
        {
            return;
        }

        foreach (var invocation in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)invocation)(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in a UpsMonitor.{Event} handler.", typeof(TArgs).Name);
            }
        }
    }
}
