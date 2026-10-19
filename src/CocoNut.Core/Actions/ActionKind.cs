namespace CocoNut.Core.Actions;

/// <summary>What a <see cref="RuleAction"/> does.</summary>
public enum ActionKind
{
    /// <summary>Shut down, suspend or hibernate this computer (see <see cref="RuleAction.StopAction"/>), with the
    /// countdown window, dry-run mode and re-arm delay of the shutdown coordinator.</summary>
    StopComputer,

    /// <summary>Shows a desktop notification with <see cref="RuleAction.Message"/> (a template, see <see cref="MessageTemplate"/>).</summary>
    Notify,

    /// <summary>Writes <see cref="RuleAction.Message"/> (a template) to the application log at <see cref="RuleAction.LogLevel"/>.</summary>
    Log,

    /// <summary>Starts <see cref="RuleAction.Command"/> with <see cref="RuleAction.Arguments"/> (no shell) and the UPS
    /// values as <c>COCONUT_*</c> environment variables; it is killed after <see cref="RuleAction.TimeoutSeconds"/>.</summary>
    RunCommand,

    /// <summary>POSTs a JSON document (<see cref="WebhookPayload"/>) to <see cref="RuleAction.Url"/>.</summary>
    Webhook,
}
