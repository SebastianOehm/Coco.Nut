using CocoNut.Core.Shutdown;

namespace CocoNut.App.Services;

/// <summary>
/// The subset of <see cref="ShutdownCoordinator"/>'s events the main window needs for its event log and
/// notifications, as an interface so tests can raise them from a fake instead of driving a real coordinator
/// (which needs a real <see cref="CocoNut.Core.Monitoring.UpsMonitor"/>). <see cref="ShutdownEventsAdapter"/> is
/// the real implementation, forwarding to a <see cref="ShutdownCoordinator"/>.
/// </summary>
public interface IShutdownEvents
{
    /// <inheritdoc cref="ShutdownCoordinator.DryRun"/>
    bool DryRun { get; }

    /// <inheritdoc cref="ShutdownCoordinator.ShutdownPending"/>
    event EventHandler<ShutdownPendingEventArgs>? ShutdownPending;

    /// <inheritdoc cref="ShutdownCoordinator.ShutdownCancelled"/>
    event EventHandler<ShutdownReason>? ShutdownCancelled;

    /// <inheritdoc cref="ShutdownCoordinator.ShutdownExecuting"/>
    event EventHandler<ShutdownReason>? ShutdownExecuting;

    /// <inheritdoc cref="ShutdownCoordinator.ShutdownFailed"/>
    event EventHandler<Exception>? ShutdownFailed;
}

/// <summary>Default <see cref="IShutdownEvents"/>, forwarding every event to a real <see cref="ShutdownCoordinator"/>.</summary>
public sealed class ShutdownEventsAdapter : IShutdownEvents
{
    private readonly ShutdownCoordinator _coordinator;

    public ShutdownEventsAdapter(ShutdownCoordinator coordinator) =>
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    /// <inheritdoc />
    public bool DryRun => _coordinator.DryRun;

    /// <inheritdoc />
    public event EventHandler<ShutdownPendingEventArgs>? ShutdownPending
    {
        add => _coordinator.ShutdownPending += value;
        remove => _coordinator.ShutdownPending -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ShutdownReason>? ShutdownCancelled
    {
        add => _coordinator.ShutdownCancelled += value;
        remove => _coordinator.ShutdownCancelled -= value;
    }

    /// <inheritdoc />
    public event EventHandler<ShutdownReason>? ShutdownExecuting
    {
        add => _coordinator.ShutdownExecuting += value;
        remove => _coordinator.ShutdownExecuting -= value;
    }

    /// <inheritdoc />
    public event EventHandler<Exception>? ShutdownFailed
    {
        add => _coordinator.ShutdownFailed += value;
        remove => _coordinator.ShutdownFailed -= value;
    }
}
