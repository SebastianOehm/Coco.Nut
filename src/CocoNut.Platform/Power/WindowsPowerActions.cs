using CocoNut.Core.Abstractions;
using CocoNut.Platform.Power.Native;
using CocoNut.Platform.Processes;
using Microsoft.Extensions.Logging;

namespace CocoNut.Platform.Power;

/// <summary>
/// Windows power actions: <c>shutdown.exe /s /f /t 0</c> for shutdown, <c>SetSuspendState</c> (via
/// <see cref="IWindowsPowerNative"/>) for suspend/hibernate. Never touches a Windows-only API directly, so
/// it can be exercised with fakes on any OS; only the factory decides whether to actually construct it.
/// </summary>
internal sealed class WindowsPowerActions : IPowerActions
{
    private readonly IWindowsPowerNative _native;
    private readonly IProcessRunner _processRunner;
    private readonly ILogger<WindowsPowerActions> _logger;

    internal WindowsPowerActions(IWindowsPowerNative native, IProcessRunner processRunner, ILogger<WindowsPowerActions> logger)
    {
        _native = native;
        _processRunner = processRunner;
        _logger = logger;
    }

    public bool IsSupported(StopAction action) => action switch
    {
        StopAction.Shutdown or StopAction.Suspend => true,
        StopAction.Hibernate => _native.IsHibernateSupported(),
        _ => false,
    };

    public async Task ExecuteAsync(StopAction action, CancellationToken cancellationToken = default)
    {
        if (!IsSupported(action))
        {
            throw new PlatformNotSupportedException($"{action} is not supported on this system.");
        }

        _logger.LogWarning("Executing power action {Action} on Windows.", action);

        switch (action)
        {
            case StopAction.Shutdown:
                await ExecuteShutdownAsync(cancellationToken).ConfigureAwait(false);
                break;
            case StopAction.Suspend:
                ExecuteSetSuspendState(hibernate: false);
                break;
            case StopAction.Hibernate:
                ExecuteSetSuspendState(hibernate: true);
                break;
            default:
                throw new PlatformNotSupportedException($"{action} is not supported on this system.");
        }
    }

    private async Task ExecuteShutdownAsync(CancellationToken cancellationToken)
    {
        var shutdownExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
        string[] arguments = ["/s", "/f", "/t", "0"];

        var result = await _processRunner.RunAsync(shutdownExe, arguments, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PowerActionException(
                $"'{shutdownExe} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode}: {result.StandardError}");
        }
    }

    private void ExecuteSetSuspendState(bool hibernate)
    {
        // Matches WinNUT: Application.SetSuspendState(state, force:=False, disableWakeEvent:=True).
        var succeeded = _native.SetSuspendState(hibernate, forceCritical: false, disableWakeEvent: true);
        if (!succeeded)
        {
            throw new PowerActionException($"SetSuspendState(hibernate: {hibernate}) failed.");
        }
    }
}
