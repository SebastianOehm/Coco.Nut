namespace CocoNut.Core.Actions;

/// <summary>Payload of <see cref="Shutdown.ShutdownCoordinator.RuleFired"/>.</summary>
public sealed class RuleFiredEventArgs : EventArgs
{
    public RuleFiredEventArgs(ActionRule rule, string detail)
    {
        Rule = rule;
        Detail = detail;
    }

    /// <summary>The rule that fired.</summary>
    public ActionRule Rule { get; }

    /// <summary>A human-readable explanation of why, suitable for logging.</summary>
    public string Detail { get; }
}
