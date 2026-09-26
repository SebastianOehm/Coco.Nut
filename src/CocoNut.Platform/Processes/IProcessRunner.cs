namespace CocoNut.Platform.Processes;

/// <summary>
/// Seam around <see cref="System.Diagnostics.Process"/> so power actions (and anything else that shells out)
/// can be unit tested without ever starting a real process.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with the given <paramref name="arguments"/> (passed as an argument
    /// vector, never concatenated into a shell command line) and waits for it to exit.
    /// </summary>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}
