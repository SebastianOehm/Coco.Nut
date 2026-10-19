namespace CocoNut.Core.Actions;

/// <summary>A program to run for <see cref="ActionKind.RunCommand"/>.</summary>
/// <param name="FileName">The executable, started directly (no shell).</param>
/// <param name="Arguments">The arguments, passed one by one.</param>
/// <param name="Environment">Environment variables added to (or replacing) the ones inherited from this process.</param>
/// <param name="Timeout">After this long the process and its child processes are killed.</param>
public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout);

/// <summary>How a command ended.</summary>
/// <param name="ExitCode">The exit code, or <see langword="null"/> when the process was killed because of the timeout.</param>
/// <param name="TimedOut">Whether the timeout elapsed and the process tree was killed.</param>
/// <param name="StandardError">What the command wrote to its standard error, cut to a few thousand characters.</param>
public sealed record CommandResult(int? ExitCode, bool TimedOut, string StandardError);

/// <summary>Runs an external program; the seam that keeps <see cref="RuleActionRunner"/> testable without starting processes.</summary>
public interface ICommandRunner
{
    /// <summary>
    /// Starts the program and waits for it to end. Throws when it cannot be started (for example the file does not
    /// exist) or when <paramref name="cancellationToken"/> is cancelled (the process tree is killed then, too).
    /// </summary>
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken);
}
