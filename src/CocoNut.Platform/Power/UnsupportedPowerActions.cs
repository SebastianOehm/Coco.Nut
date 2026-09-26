using CocoNut.Core.Abstractions;

namespace CocoNut.Platform.Power;

/// <summary>Fallback for an operating system we have no power actions for (e.g. FreeBSD).</summary>
internal sealed class UnsupportedPowerActions : IPowerActions
{
    public bool IsSupported(StopAction action) => false;

    public Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException($"{action} is not supported on this operating system.");
}
