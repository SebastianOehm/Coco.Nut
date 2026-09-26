namespace CocoNut.Platform.Power;

/// <summary>An <see cref="Abstractions.IPowerActions"/> command was run but the operating system reported failure.</summary>
public sealed class PowerActionException : Exception
{
    public PowerActionException(string message)
        : base(message)
    {
    }

    public PowerActionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
