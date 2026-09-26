using CocoNut.Core.Abstractions;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging;

namespace CocoNut.Platform.Power;

/// <summary>
/// macOS power actions: <c>osascript</c> for shutdown (there is no unprivileged "shutdown now" CLI command),
/// <c>pmset sleepnow</c> for suspend. macOS has no user-triggerable hibernate, so that action is unsupported.
/// </summary>
internal sealed class MacPowerActions : IPowerActions
{
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<MacPowerActions> _logger;

    internal MacPowerActions(IProcessRunner processRunner, ILogger<MacPowerActions> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public bool IsSupported(StopAction action) => action != StopAction.Hibernate;

    public async Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(action))
        {
            throw new PlatformNotSupportedException($"{action} is not supported on macOS.");
        }

        _logger.LogWarning("Executing power action {Action} on macOS.", action);

        (string fileName, string[] arguments) = action switch
        {
            StopAction.Shutdown => ("osascript", new[] { "-e", "tell application \"System Events\" to shut down" }),
            StopAction.Suspend => ("pmset", new[] { "sleepnow" }),
            _ => throw new PlatformNotSupportedException($"{action} is not supported on macOS."),
        };

        var result = await _processRunner.RunAsync(fileName, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PowerActionException(
                $"'{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.StandardError}");
        }
    }
}
