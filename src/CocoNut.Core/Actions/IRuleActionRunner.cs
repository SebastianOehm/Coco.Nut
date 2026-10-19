using CocoNut.Core.Ups;

namespace CocoNut.Core.Actions;

/// <summary>Runs the actions of a fired rule other than <see cref="ActionKind.StopComputer"/> (which the
/// <see cref="Shutdown.ShutdownCoordinator"/> handles itself).</summary>
public interface IRuleActionRunner
{
    /// <summary>
    /// Starts the notify, log, command and webhook actions of <paramref name="match"/>'s rule and returns at once.
    /// Each action runs on its own and a failure or timeout of one never reaches the caller or the other actions.
    /// </summary>
    /// <param name="match">The firing rule.</param>
    /// <param name="reading">The reading at that moment, or <see langword="null"/> when there is none.</param>
    /// <param name="upsName">The name of the monitored UPS.</param>
    void Run(RuleMatch match, UpsReading? reading, string upsName);
}
