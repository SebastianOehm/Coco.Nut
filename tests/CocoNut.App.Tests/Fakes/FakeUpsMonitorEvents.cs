using CocoNut.App.Services;
using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Drives <see cref="IUpsMonitorEvents"/> consumers without a real <see cref="UpsMonitor"/>/NUT server.</summary>
public sealed class FakeUpsMonitorEvents : IUpsMonitorEvents
{
    public MonitorState State { get; private set; } = MonitorState.Disconnected;

    public UpsInfo? Info { get; set; }

    public UpsReading? LastReading { get; private set; }

    public ConnectionSettings? Settings { get; set; } = new() { Host = "nutserver", Port = 3493 };

    public event EventHandler<MonitorStateChangedEventArgs>? StateChanged;

    public event EventHandler<UpsReading>? ReadingUpdated;

    public event EventHandler<UpsStatusChangedEventArgs>? StatusChanged;

    public event EventHandler<Exception?>? ConnectionLost;

    public event EventHandler<int>? ReconnectAttempt;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public Task StartAsync(ConnectionSettings connection, double nominalFrequency, CancellationToken cancellationToken = default)
    {
        StartCount++;
        Settings = connection;
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        StopCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NutVariable>> GetAllVariablesAsync(bool includeDescriptions, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NutVariable>>([]);

    public void RaiseStateChanged(MonitorState oldState, MonitorState newState, Exception? error = null)
    {
        State = newState;
        StateChanged?.Invoke(this, new MonitorStateChangedEventArgs(oldState, newState, error));
    }

    public void RaiseReadingUpdated(UpsReading reading)
    {
        LastReading = reading;
        ReadingUpdated?.Invoke(this, reading);
    }

    public void RaiseStatusChanged(UpsStatus previous, UpsStatus current)
    {
        var newlyActive = current & ~previous;
        var newlyCleared = previous & ~current;
        StatusChanged?.Invoke(this, new UpsStatusChangedEventArgs(previous, current, newlyActive, newlyCleared));
    }

    public void RaiseConnectionLost(Exception? error = null) => ConnectionLost?.Invoke(this, error);

    public void RaiseReconnectAttempt(int attempt) => ReconnectAttempt?.Invoke(this, attempt);
}
