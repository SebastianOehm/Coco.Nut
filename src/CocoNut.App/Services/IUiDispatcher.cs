using Avalonia.Threading;

namespace CocoNut.App.Services;

/// <summary>
/// Marshals a callback onto the UI thread. Every <see cref="CocoNut.Core.Monitoring.UpsMonitor"/>/
/// <see cref="CocoNut.Core.Shutdown.ShutdownCoordinator"/> event is raised on a thread-pool thread (see their
/// XML docs), so view models post through this before touching any bound property. Abstracted so unit tests can
/// run handlers synchronously (see <c>ImmediateUiDispatcher</c> in the test project) instead of needing a pumped
/// Avalonia dispatcher loop.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Schedules <paramref name="action"/> to run on the UI thread and returns immediately.</summary>
    void Post(Action action);
}

/// <summary>Default <see cref="IUiDispatcher"/>, backed by <see cref="Dispatcher.UIThread"/>.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
