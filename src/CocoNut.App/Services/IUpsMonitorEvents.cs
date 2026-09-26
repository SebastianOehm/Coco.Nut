using CocoNut.Core.Monitoring;
using CocoNut.Core.Nut;
using CocoNut.Core.Settings;
using CocoNut.Core.Ups;

namespace CocoNut.App.Services;

/// <summary>
/// The subset of <see cref="UpsMonitor"/> the app-shell view models and services need, as an interface so tests
/// can drive them with a fake instead of a real <see cref="UpsMonitor"/> (which needs a live NUT connection to do
/// anything). <see cref="UpsMonitorAdapter"/> is the real implementation, forwarding to a <see cref="UpsMonitor"/>.
/// </summary>
public interface IUpsMonitorEvents
{
    /// <inheritdoc cref="UpsMonitor.State"/>
    MonitorState State { get; }

    /// <inheritdoc cref="UpsMonitor.Info"/>
    UpsInfo? Info { get; }

    /// <inheritdoc cref="UpsMonitor.LastReading"/>
    UpsReading? LastReading { get; }

    /// <inheritdoc cref="UpsMonitor.Settings"/>
    ConnectionSettings? Settings { get; }

    /// <inheritdoc cref="UpsMonitor.StateChanged"/>
    event EventHandler<MonitorStateChangedEventArgs>? StateChanged;

    /// <inheritdoc cref="UpsMonitor.ReadingUpdated"/>
    event EventHandler<UpsReading>? ReadingUpdated;

    /// <inheritdoc cref="UpsMonitor.StatusChanged"/>
    event EventHandler<UpsStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc cref="UpsMonitor.ConnectionLost"/>
    event EventHandler<Exception?>? ConnectionLost;

    /// <inheritdoc cref="UpsMonitor.ReconnectAttempt"/>
    event EventHandler<int>? ReconnectAttempt;

    /// <inheritdoc cref="UpsMonitor.StartAsync"/>
    Task StartAsync(ConnectionSettings connection, double nominalFrequency, CancellationToken cancellationToken = default);

    /// <inheritdoc cref="UpsMonitor.StopAsync"/>
    Task StopAsync();

    /// <inheritdoc cref="UpsMonitor.GetAllVariablesAsync"/>
    Task<IReadOnlyList<NutVariable>> GetAllVariablesAsync(bool includeDescriptions, CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="IUpsMonitorEvents"/>, forwarding every member to a real <see cref="UpsMonitor"/>.</summary>
public sealed class UpsMonitorAdapter : IUpsMonitorEvents
{
    private readonly UpsMonitor _monitor;

    public UpsMonitorAdapter(UpsMonitor monitor) => _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));

    /// <inheritdoc />
    public MonitorState State => _monitor.State;

    /// <inheritdoc />
    public UpsInfo? Info => _monitor.Info;

    /// <inheritdoc />
    public UpsReading? LastReading => _monitor.LastReading;

    /// <inheritdoc />
    public ConnectionSettings? Settings => _monitor.Settings;

    /// <inheritdoc />
    public event EventHandler<MonitorStateChangedEventArgs>? StateChanged
    {
        add => _monitor.StateChanged += value;
        remove => _monitor.StateChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<UpsReading>? ReadingUpdated
    {
        add => _monitor.ReadingUpdated += value;
        remove => _monitor.ReadingUpdated -= value;
    }

    /// <inheritdoc />
    public event EventHandler<UpsStatusChangedEventArgs>? StatusChanged
    {
        add => _monitor.StatusChanged += value;
        remove => _monitor.StatusChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<Exception?>? ConnectionLost
    {
        add => _monitor.ConnectionLost += value;
        remove => _monitor.ConnectionLost -= value;
    }

    /// <inheritdoc />
    public event EventHandler<int>? ReconnectAttempt
    {
        add => _monitor.ReconnectAttempt += value;
        remove => _monitor.ReconnectAttempt -= value;
    }

    /// <inheritdoc />
    public Task StartAsync(ConnectionSettings connection, double nominalFrequency, CancellationToken cancellationToken = default) =>
        _monitor.StartAsync(connection, nominalFrequency, cancellationToken);

    /// <inheritdoc />
    public Task StopAsync() => _monitor.StopAsync();

    /// <inheritdoc />
    public Task<IReadOnlyList<NutVariable>> GetAllVariablesAsync(bool includeDescriptions, CancellationToken cancellationToken = default) =>
        _monitor.GetAllVariablesAsync(includeDescriptions, cancellationToken);
}
