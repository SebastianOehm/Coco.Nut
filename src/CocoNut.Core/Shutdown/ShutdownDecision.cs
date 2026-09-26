namespace CocoNut.Core.Shutdown;

/// <summary>What <see cref="ShutdownPolicy.Evaluate"/> concluded a UPS reading requires.</summary>
public enum ShutdownDecisionKind
{
    /// <summary>No action is required.</summary>
    None,

    /// <summary>The stop procedure should start (or, if already pending, keep running - never started twice).</summary>
    Start,

    /// <summary>A pending stop procedure should be cancelled.</summary>
    Cancel,
}

/// <summary>Result of evaluating one <see cref="Ups.UpsReading"/> against <see cref="Settings.PowerSettings"/>.</summary>
/// <param name="Kind">What should happen.</param>
/// <param name="Reason">Why (meaningful only when <paramref name="Kind"/> is not <see cref="ShutdownDecisionKind.None"/>).</param>
/// <param name="Detail">A human-readable explanation, suitable for logging.</param>
public sealed record ShutdownDecision(ShutdownDecisionKind Kind, ShutdownReason Reason, string Detail)
{
    /// <summary>The "nothing to do" decision.</summary>
    public static ShutdownDecision None { get; } = new(ShutdownDecisionKind.None, ShutdownReason.None, string.Empty);
}
