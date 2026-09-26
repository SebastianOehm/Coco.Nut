using CocoNut.App.Services;
using CocoNut.Core.Shutdown;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Drives <see cref="IShutdownEvents"/> consumers without a real <see cref="ShutdownCoordinator"/>.</summary>
public sealed class FakeShutdownEvents : IShutdownEvents
{
    public event EventHandler<ShutdownPendingEventArgs>? ShutdownPending;

    public event EventHandler<ShutdownReason>? ShutdownCancelled;

    public event EventHandler<ShutdownReason>? ShutdownExecuting;

    public event EventHandler<Exception>? ShutdownFailed;

    public void RaisePending(ShutdownPendingEventArgs e) => ShutdownPending?.Invoke(this, e);

    public void RaiseCancelled(ShutdownReason reason) => ShutdownCancelled?.Invoke(this, reason);

    public void RaiseExecuting(ShutdownReason reason) => ShutdownExecuting?.Invoke(this, reason);

    public void RaiseFailed(Exception exception) => ShutdownFailed?.Invoke(this, exception);
}
