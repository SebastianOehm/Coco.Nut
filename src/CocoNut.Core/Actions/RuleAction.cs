using CocoNut.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CocoNut.Core.Actions;

/// <summary>One thing an <see cref="ActionRule"/> does when it fires. Which properties matter depends on
/// <see cref="Kind"/>; the others are ignored.</summary>
public sealed class RuleAction
{
    /// <summary>The default for <see cref="TimeoutSeconds"/>.</summary>
    public const int DefaultTimeoutSeconds = 60;

    /// <summary>What to do.</summary>
    public ActionKind Kind { get; set; } = ActionKind.StopComputer;

    /// <summary>The power action for <see cref="ActionKind.StopComputer"/>.</summary>
    public StopAction StopAction { get; set; } = StopAction.Shutdown;

    /// <summary>The message template for <see cref="ActionKind.Notify"/> and <see cref="ActionKind.Log"/>, see <see cref="MessageTemplate"/>.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>The level for <see cref="ActionKind.Log"/>.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>The executable for <see cref="ActionKind.RunCommand"/>. Started directly, not through a shell.</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>The arguments for <see cref="ActionKind.RunCommand"/>, passed one by one (no quoting or shell parsing).</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>For <see cref="ActionKind.RunCommand"/>: the command (with its child processes) is killed after this many seconds.</summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>The absolute http/https address for <see cref="ActionKind.Webhook"/>.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Creates a <see cref="ActionKind.StopComputer"/> action.</summary>
    public static RuleAction StopComputer(StopAction stopAction) => new() { Kind = ActionKind.StopComputer, StopAction = stopAction };

    /// <summary>Creates a <see cref="ActionKind.Notify"/> action.</summary>
    public static RuleAction Notify(string message) => new() { Kind = ActionKind.Notify, Message = message };

    /// <summary>Creates a <see cref="ActionKind.Log"/> action.</summary>
    public static RuleAction Log(string message, LogLevel level = LogLevel.Information) =>
        new() { Kind = ActionKind.Log, Message = message, LogLevel = level };

    /// <summary>Creates a <see cref="ActionKind.RunCommand"/> action.</summary>
    public static RuleAction RunCommand(string command, IEnumerable<string>? arguments = null, int timeoutSeconds = DefaultTimeoutSeconds) =>
        new() { Kind = ActionKind.RunCommand, Command = command, Arguments = [.. arguments ?? []], TimeoutSeconds = timeoutSeconds };

    /// <summary>Creates a <see cref="ActionKind.Webhook"/> action.</summary>
    public static RuleAction Webhook(string url) => new() { Kind = ActionKind.Webhook, Url = url };
}
