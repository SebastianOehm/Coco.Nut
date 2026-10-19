using CocoNut.Core.Shutdown;

namespace CocoNut.Core.Actions;

/// <summary>
/// A rule: when <see cref="Trigger"/> matches (and has held for <see cref="DelaySeconds"/>), run <see cref="Actions"/>.
/// Rules are evaluated in list order, which is also their priority: the first firing stop rule decides the reason.
/// </summary>
public sealed class ActionRule
{
    /// <summary>Stable identity; the <see cref="RuleEngine"/> keys its per-rule state by this.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Disabled rules never fire.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The UPS this rule applies to; <see langword="null"/> = the UPS this computer monitors / is powered by. Only
    /// <see langword="null"/> is evaluated for now, rules with a name are ignored until several UPSes are supported.
    /// </summary>
    public string? UpsName { get; set; }

    /// <summary>The condition.</summary>
    public RuleTrigger Trigger { get; set; } = new();

    /// <summary>Seconds a level condition must hold continuously before the rule fires; 0 = at once. Ignored for edge triggers.</summary>
    public int DelaySeconds { get; set; }

    /// <summary>The reason reported (countdown window, history, notifications) when this rule starts a stop.
    /// Built-in rules carry their specific reason, user rules <see cref="Shutdown.ShutdownReason.CustomRule"/>.</summary>
    public ShutdownReason ShutdownReason { get; set; } = ShutdownReason.CustomRule;

    /// <summary>What to do when the rule fires.</summary>
    public List<RuleAction> Actions { get; set; } = [];

    /// <summary>Whether any action stops (shuts down, suspends, hibernates) this computer.</summary>
    public bool StopsComputer() => Actions.Any(a => a.Kind == ActionKind.StopComputer);
}
