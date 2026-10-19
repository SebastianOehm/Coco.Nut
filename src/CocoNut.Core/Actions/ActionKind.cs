namespace CocoNut.Core.Actions;

/// <summary>What a <see cref="RuleAction"/> does.</summary>
public enum ActionKind
{
    /// <summary>Shut down, suspend or hibernate this computer (see <see cref="RuleAction.StopAction"/>), with the
    /// countdown window, dry-run mode and re-arm delay of the shutdown coordinator.</summary>
    StopComputer,
}
