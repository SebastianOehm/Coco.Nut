using CocoNut.Core.Abstractions;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging;

namespace CocoNut.Platform.Power;

/// <summary>
/// Linux power actions via <c>systemctl</c> (works for a logged-in user through polkit/logind, same as a
/// desktop session's own power button).
/// </summary>
internal sealed class LinuxPowerActions : IPowerActions
{
    private const string SystemCtl = "systemctl";

    private readonly IProcessRunner _processRunner;
    private readonly IExecutableLocator _executableLocator;
    private readonly ILogger<LinuxPowerActions> _logger;

    internal LinuxPowerActions(IProcessRunner processRunner, IExecutableLocator executableLocator, ILogger<LinuxPowerActions> logger)
    {
        _processRunner = processRunner;
        _executableLocator = executableLocator;
        _logger = logger;
    }

    public bool IsSupported(StopAction action) => _executableLocator.ExistsOnPath(SystemCtl);

    public async Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(action))
        {
            throw new PlatformNotSupportedException($"{action} is not supported: '{SystemCtl}' was not found on PATH.");
        }

        var argument = action switch
        {
            StopAction.Shutdown => "poweroff",
            StopAction.Suspend => "suspend",
            StopAction.Hibernate => "hibernate",
            _ => throw new PlatformNotSupportedException($"{action} is not supported on this system."),
        };

        _logger.LogWarning("Executing power action {Action} via '{Command} {Argument}'.", action, SystemCtl, argument);

        var result = await _processRunner.RunAsync(SystemCtl, [argument], cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PowerActionException(
                $"'{SystemCtl} {argument}' failed with exit code {result.ExitCode}: {result.StandardError}");
        }
    }
}
