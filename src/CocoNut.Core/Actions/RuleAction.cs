using CocoNut.Core.Abstractions;

namespace CocoNut.Core.Actions;

/// <summary>One thing an <see cref="ActionRule"/> does when it fires.</summary>
public sealed class RuleAction
{
    /// <summary>What to do.</summary>
    public ActionKind Kind { get; set; } = ActionKind.StopComputer;

    /// <summary>The power action for <see cref="ActionKind.StopComputer"/>.</summary>
    public StopAction StopAction { get; set; } = StopAction.Shutdown;

    /// <summary>Creates a <see cref="ActionKind.StopComputer"/> action.</summary>
    public static RuleAction StopComputer(StopAction stopAction) => new() { Kind = ActionKind.StopComputer, StopAction = stopAction };
}
