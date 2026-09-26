namespace CocoNut.Core.Nut;

/// <summary>
/// The NUT server answered with <c>ERR ...</c> or with a response that could not be understood.
/// Transport problems (socket closed, timeouts) are reported as <see cref="IOException"/> instead.
/// </summary>
public sealed class NutException : Exception
{
    public NutException(NutErrorCode errorCode, string? query, string? rawResponse)
        : base($"NUT error {errorCode} ({rawResponse}) for query: {query}")
    {
        ErrorCode = errorCode;
        Query = query;
        RawResponse = rawResponse;
    }

    public NutErrorCode ErrorCode { get; }

    /// <summary>The query that was sent, with any password masked.</summary>
    public string? Query { get; }

    public string? RawResponse { get; }
}
