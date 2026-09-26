namespace CocoNut.Core.Monitoring;

/// <summary>Lifecycle state of a <see cref="UpsMonitor"/>.</summary>
public enum MonitorState
{
    /// <summary>Not connected and not trying to connect (initial state, or after a graceful/fatal stop).</summary>
    Disconnected,
    /// <summary>The first connection attempt (connect, VER/NETVER, authenticate, login) is in progress.</summary>
    Connecting,
    /// <summary>Logged in and polling.</summary>
    Connected,
    /// <summary>An established connection was lost and automatic reconnection is being attempted.</summary>
    Reconnecting,
}
