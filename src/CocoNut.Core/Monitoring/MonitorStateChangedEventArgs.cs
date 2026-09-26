namespace CocoNut.Core.Monitoring;

/// <summary>Payload of <see cref="UpsMonitor.StateChanged"/>.</summary>
public sealed class MonitorStateChangedEventArgs : EventArgs
{
    public MonitorStateChangedEventArgs(MonitorState oldState, MonitorState newState, Exception? error)
    {
        OldState = oldState;
        NewState = newState;
        Error = error;
    }

    public MonitorState OldState { get; }

    public MonitorState NewState { get; }

    /// <summary>The error that caused the transition, if any (e.g. a failed login or a transport error).</summary>
    public Exception? Error { get; }
}
