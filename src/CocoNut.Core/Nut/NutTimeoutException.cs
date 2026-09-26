namespace CocoNut.Core.Nut;

/// <summary>
/// A request to the NUT server did not complete within <see cref="NutClient"/>'s configured timeout.
/// This is a transport failure: it derives from <see cref="IOException"/> (so code that already
/// catches <see cref="IOException"/> per <see cref="INutClient"/>'s error contract handles it too),
/// while still letting callers that care distinguish "timed out" from other I/O errors.
/// The connection is closed and <see cref="INutClient.ConnectionLost"/> is raised, exactly like any
/// other transport failure.
/// </summary>
public sealed class NutTimeoutException : IOException
{
    /// <summary>Initializes a new instance of <see cref="NutTimeoutException"/>.</summary>
    public NutTimeoutException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
