using CocoNut.Core.Abstractions;

namespace CocoNut.App.Tests.Fakes;

/// <summary>Drives <see cref="IPowerActions"/> consumers with a configurable set of "supported" actions.</summary>
public sealed class FakePowerActions : IPowerActions
{
    /// <summary>Actions <see cref="IsSupported"/> reports as supported. Defaults to all three.</summary>
    public HashSet<StopAction> Supported { get; } = [StopAction.Shutdown, StopAction.Suspend, StopAction.Hibernate];

    public List<StopAction> ExecutedActions { get; } = [];

    public bool IsSupported(StopAction action) => Supported.Contains(action);

    public Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
    {
        ExecutedActions.Add(action);
        return Task.CompletedTask;
    }
}
