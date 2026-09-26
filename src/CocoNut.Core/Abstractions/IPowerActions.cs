namespace CocoNut.Core.Abstractions;

/// <summary>What to do with the computer when the stop conditions are met. Values match WinNUT's <c>PW_StopType</c>.</summary>
public enum StopAction
{
    Shutdown = 0,
    Suspend = 1,
    Hibernate = 2,
}

/// <summary>Operating-system specific power operations.</summary>
public interface IPowerActions
{
    bool IsSupported(StopAction action);

    /// <summary>Executes the action. Throws <see cref="PlatformNotSupportedException"/> when unsupported.</summary>
    Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default);
}
